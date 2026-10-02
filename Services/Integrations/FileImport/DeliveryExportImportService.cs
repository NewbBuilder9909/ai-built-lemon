using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Integrations.FileImport;

/// <summary>One problem with one cell. Row is the 1-based line in the uploaded file (the header is row 1).</summary>
public sealed record FileImportRowError(int Row, string Column, string Message);

/// <summary>
/// The outcome of one upload. <see cref="Imported"/> false means nothing was
/// written — validation is all-or-nothing, so a half-imported file can never
/// sit behind a diagnostic's figures. A file that would remove rows is not
/// written either: it comes back with a <see cref="Plan"/> and a
/// <see cref="StagingKey"/>, waiting for the person to confirm.
/// </summary>
public sealed record FileImportResult(
    bool Imported,
    string Summary,
    IReadOnlyList<FileImportRowError> Errors,
    int Rows = 0,
    int UnresolvedPeople = 0,
    int UnmappedStatuses = 0,
    IReadOnlyList<string>? IgnoredColumns = null,
    ImportPlan? Plan = null,
    Guid? StagingKey = null,
    DeliveryExportKind? Kind = null)
{
    public bool AwaitingConfirmation => StagingKey is not null;

    public static FileImportResult Rejected(string summary, IReadOnlyList<FileImportRowError>? errors = null, IReadOnlyList<string>? ignored = null) =>
        new(false, summary, errors ?? [], IgnoredColumns: ignored);
}

/// <summary>One row that is in the product but not in the new file, so a replacement removes it.</summary>
public sealed record ImportRemoval(string Id, string Description);

/// <summary>
/// What a replacement import would change, shown before anything is removed.
/// Counts are rows of this source only; <see cref="HoursBefore"/> and
/// <see cref="HoursAfter"/> are set for time entries. For work items,
/// <see cref="HoursUnlinkedByRemoval"/> is the recorded time, from any
/// source, that would lose its work item (it is kept, not deleted).
/// </summary>
public sealed record ImportPlan(
    DeliveryExportKind Kind,
    int RowsBefore,
    int RowsAfter,
    int Added,
    int Updated,
    int Removed,
    IReadOnlyList<ImportRemoval> Removals,
    decimal? HoursBefore,
    decimal? HoursAfter,
    decimal HoursUnlinkedByRemoval,
    string Fingerprint)
{
    public int RemovalsNotShown => Removed - Removals.Count;
}

public interface IDeliveryExportImportService
{
    /// <summary>
    /// The file replaces everything previously imported of its kind. With no
    /// removals it is written at once; otherwise it is staged and returned
    /// with a plan for <see cref="ConfirmAsync"/>.
    /// </summary>
    Task<FileImportResult> ImportAsync(Guid tenantId, DeliveryExportKind kind, string csvText, int? triggeredByMemberId, CancellationToken cancellationToken = default);

    /// <summary>Applies a staged replacement, provided the plan is still what the person saw.</summary>
    Task<FileImportResult> ConfirmAsync(Guid tenantId, Guid stagingKey, int? triggeredByMemberId, CancellationToken cancellationToken = default);

    Task CancelAsync(Guid tenantId, Guid stagingKey, int? triggeredByMemberId);
}

/// <summary>
/// Imports a customer's sanitised delivery export — the assisted-diagnostic
/// entry path, where the customer shares files instead of API credentials
/// (docs/commercial/paid-diagnostic-offer.md). A Bronze source like any
/// other: rows are captured verbatim to ProgrammeOps_RawConnectorPayload,
/// mapped into the same Silver tables every connector writes, under the
/// same SyncRun lease and audit trail, so Gold treats imported data exactly
/// like synced data and its provenance still says where it came from.
///
/// Two rules the connectors already follow, kept here too:
/// people go through <see cref="IStaffIdentityResolver"/>, so someone nobody
/// can match lands on the identity queue rather than vanishing; and an
/// unrecognised status stays Unmapped rather than being guessed into
/// progress. Upserts key on (tenant, "FileImport", id), so the same file
/// twice changes nothing.
///
/// <b>Each upload replaces</b> everything previously imported of its kind:
/// a row that is in the product but not in the file is removed. A weekly
/// diagnostic re-exports the whole period, so a missing row means it was
/// deleted or corrected at source; keeping it would leave stale or doubled
/// hours behind the figures (an edited entry with no EntryId gets a new
/// derived id, so its old version must go). Because a partial export would
/// remove too much, nothing is removed without a preview: a file that would
/// remove rows is staged, and applied only when the person confirms the
/// counts and hours they were shown. Removed work items take their
/// allocations, dependencies and baselines with them; time recorded against
/// them is unlinked, never deleted. The whole apply is one transaction.
/// </summary>
public sealed class DeliveryExportImportService(
    IProgrammeRepository programmes,
    IRawConnectorPayloadRepository raw,
    IStaffIdentityResolver identities,
    IAuditLogRepository audit,
    SyncRunCoordinator runs,
    IOptions<ProgrammeOpsOptions> options,
    TimeProvider clock,
    IImportStagingRepository staging,
    // Null only in fake-backed unit tests, which have no database to wrap.
    IScopeProvider? scopes = null) : IDeliveryExportImportService
{
    public const string SourceName = "FileImport";
    public const int MaxDataRows = 20_000;
    public const int RemovalsShown = 50;

    /// <summary>How long a preview can be confirmed. Staged files older than a day are purged.</summary>
    public static readonly TimeSpan StagingLifetime = TimeSpan.FromHours(1);

    private const int MaxReportedErrors = 200;
    private const string DefaultWorkstream = "Work items";
    private const string RootProgrammeExternalId = "fileimport-root";
    private const string DerivedIdPrefix = "derived:";

    public async Task<FileImportResult> ImportAsync(Guid tenantId, DeliveryExportKind kind, string csvText, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var (rejected, prepared) = await PrepareAsync(tenantId, kind, csvText);
        if (rejected is not null)
            return await RejectAsync(tenantId, kind, triggeredByMemberId, rejected);

        var (plan, targets) = await PlanAsync(tenantId, prepared!, cancellationToken);
        if (plan.Removed == 0)
            return await ApplyAsync(tenantId, triggeredByMemberId, prepared!, plan, targets, null, cancellationToken);
        return await StageAsync(tenantId, csvText, prepared!, plan, triggeredByMemberId);
    }

    public async Task<FileImportResult> ConfirmAsync(Guid tenantId, Guid stagingKey, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var staged = await staging.GetAsync(tenantId, stagingKey);
        if (staged is null || !Enum.TryParse<DeliveryExportKind>(staged.Kind, out var kind))
            return FileImportResult.Rejected("This preview is no longer available: it was already confirmed or cancelled, or it expired. Upload the file again.");
        if (clock.GetUtcNow().UtcDateTime - staged.CreatedAtUtc > StagingLifetime)
        {
            await staging.DeleteAsync(tenantId, stagingKey);
            return FileImportResult.Rejected($"This preview expired after {StagingLifetime.TotalMinutes:N0} minutes, so nothing was changed. Upload the file again.") with { Kind = kind };
        }

        // Re-check everything: work items may have changed since the preview.
        var (rejected, prepared) = await PrepareAsync(tenantId, kind, staged.Content);
        if (rejected is not null)
        {
            await staging.DeleteAsync(tenantId, stagingKey);
            return await RejectAsync(tenantId, kind, triggeredByMemberId, rejected);
        }

        var (plan, targets) = await PlanAsync(tenantId, prepared!, cancellationToken);
        if (plan.Fingerprint != staged.PlanFingerprint)
        {
            // Never apply removals the person didn't see: show the new figures instead.
            await staging.DeleteAsync(tenantId, stagingKey);
            var fresh = await StageAsync(tenantId, staged.Content, prepared!, plan, triggeredByMemberId);
            return fresh with { Summary = "The data changed since the preview, so nothing was applied. These are the new figures; check them and confirm again." };
        }

        return await ApplyAsync(tenantId, triggeredByMemberId, prepared!, plan, targets, stagingKey, cancellationToken);
    }

    public async Task CancelAsync(Guid tenantId, Guid stagingKey, int? triggeredByMemberId)
    {
        await staging.DeleteAsync(tenantId, stagingKey);
        await audit.LogAsync("FileImport", stagingKey.ToString(), "ImportCancelled", triggeredByMemberId,
            JsonSerializer.Serialize(new { stagingKey }), clock.GetUtcNow().UtcDateTime, tenantId);
    }

    // ---- Preparation, plan and staging --------------------------------------

    /// <summary>A validated file, ready to plan and write. Time rows already carry their external ids.</summary>
    private sealed record Prepared(
        DeliveryExportKind Kind,
        IReadOnlyList<string> Ignored,
        IReadOnlyList<WorkItemRow> WorkItems,
        IReadOnlyList<TimeEntryRow> TimeEntries,
        IReadOnlyDictionary<string, Guid> WorkItemKeys);

    /// <summary>The keys behind a plan's removals. Kept out of <see cref="ImportPlan"/>, which is for display.</summary>
    private sealed record RemovalTargets(IReadOnlyList<Guid> WorkItemKeys, IReadOnlyList<string> TimeEntryIds);

    private async Task<(FileImportResult? Rejected, Prepared? Prepared)> PrepareAsync(Guid tenantId, DeliveryExportKind kind, string csvText)
    {
        IReadOnlyList<string[]> rows;
        try
        {
            rows = DelimitedTextParser.Parse(csvText, MaxDataRows + 1);
        }
        catch (FileImportFormatException ex)
        {
            return (FileImportResult.Rejected(ex.Message), null);
        }

        if (rows.Count < 2)
            return (FileImportResult.Rejected("The file has no data rows. The first row must be column headers, followed by at least one row of data."), null);

        var header = BindHeader(rows[0], DeliveryExportSchema.ColumnsFor(kind), out var headerErrors, out var ignored);
        if (headerErrors.Count > 0)
            return (FileImportResult.Rejected("The column headers don't match the template. Nothing was imported.", headerErrors, ignored), null);

        return kind == DeliveryExportKind.WorkItems
            ? PrepareWorkItems(rows, header, ignored)
            : await PrepareTimeEntriesAsync(tenantId, rows, header, ignored);
    }

    private async Task<(ImportPlan Plan, RemovalTargets Targets)> PlanAsync(Guid tenantId, Prepared prepared, CancellationToken cancellationToken)
    {
        if (prepared.Kind == DeliveryExportKind.WorkItems)
        {
            var existing = (await programmes.GetWorkItemsAsync(tenantId, cancellationToken)).Where(i => i.ExternalSource == SourceName).ToList();
            var incoming = prepared.WorkItems.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = existing.Where(i => i.ExternalId is null || !incoming.Contains(i.ExternalId))
                .OrderBy(i => i.ExternalId, StringComparer.OrdinalIgnoreCase).ToList();
            var kept = existing.Count - removed.Count;
            var unlinked = 0m;
            if (removed.Count > 0)
            {
                var hours = await programmes.GetLoggedHoursByWorkItemAsync(tenantId, cancellationToken: cancellationToken);
                unlinked = removed.Sum(i => hours.GetValueOrDefault(i.WorkItemKey));
            }
            var plan = new ImportPlan(prepared.Kind, existing.Count, prepared.WorkItems.Count, prepared.WorkItems.Count - kept, kept, removed.Count,
                removed.Take(RemovalsShown).Select(i => new ImportRemoval(i.ExternalId ?? "(no id)", i.Title)).ToList(),
                null, null, unlinked, Fingerprint(prepared.Kind, existing.Count, prepared.WorkItems.Count, removed.Select(i => i.ExternalId ?? i.WorkItemKey.ToString())));
            return (plan, new RemovalTargets(removed.Select(i => i.WorkItemKey).ToList(), []));
        }
        else
        {
            var existing = await programmes.GetTimeEntriesBySourceAsync(tenantId, SourceName, cancellationToken);
            var incoming = prepared.TimeEntries.Select(r => r.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = existing.Where(t => t.ExternalId is not null && !incoming.Contains(t.ExternalId))
                .OrderBy(t => t.WorkDate).ThenBy(t => t.ExternalId, StringComparer.OrdinalIgnoreCase).ToList();
            var kept = existing.Count(t => t.ExternalId is not null && incoming.Contains(t.ExternalId));
            var plan = new ImportPlan(prepared.Kind, existing.Count, prepared.TimeEntries.Count, prepared.TimeEntries.Count - kept, kept, removed.Count,
                removed.Take(RemovalsShown).Select(t => new ImportRemoval(
                    t.ExternalId!.StartsWith(DerivedIdPrefix, StringComparison.Ordinal) ? "(no EntryId)" : t.ExternalId,
                    $"{t.WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "undated"}, {t.DurationHours:0.##} h")).ToList(),
                existing.Sum(t => t.DurationHours), prepared.TimeEntries.Sum(r => r.Hours), 0m,
                Fingerprint(prepared.Kind, existing.Count, prepared.TimeEntries.Count, removed.Select(t => t.ExternalId!)));
            return (plan, new RemovalTargets([], removed.Select(t => t.ExternalId!).ToList()));
        }
    }

    /// <summary>What the preview promised: the removal set and the counts around it. Confirm refuses if it has changed.</summary>
    private static string Fingerprint(DeliveryExportKind kind, int before, int after, IEnumerable<string> removedIds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f',
            new[] { kind.ToString(), before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture) }
                .Concat(removedIds.Select(id => id.ToUpperInvariant()).Order(StringComparer.Ordinal))))));

    private async Task<FileImportResult> StageAsync(Guid tenantId, string csvText, Prepared prepared, ImportPlan plan, int? memberId)
    {
        var key = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        await staging.AddAsync(new StagedImport(key, tenantId, prepared.Kind.ToString(), csvText, plan.Fingerprint, memberId, now));
        await audit.LogAsync("FileImport", prepared.Kind.ToString(), "ImportStaged", memberId,
            JsonSerializer.Serialize(new { stagingKey = key, plan.RowsBefore, plan.RowsAfter, plan.Added, plan.Updated, plan.Removed, plan.HoursBefore, plan.HoursAfter, plan.HoursUnlinkedByRemoval }),
            now, tenantId);
        await staging.DeleteOlderThanAsync(now.AddDays(-1));

        var noun = prepared.Kind == DeliveryExportKind.WorkItems ? "work item" : "time entry";
        var plural = prepared.Kind == DeliveryExportKind.WorkItems ? "work items" : "time entries";
        return new FileImportResult(false,
            $"This file would remove {plan.Removed:N0} {(plan.Removed == 1 ? noun : plural)} that {(plan.Removed == 1 ? "is" : "are")} in the product but not in the file. Nothing has changed yet: check the figures below, then confirm or cancel.",
            [], plan.RowsAfter, IgnoredColumns: prepared.Ignored, Plan: plan, StagingKey: key, Kind: prepared.Kind);
    }

    private Task<FileImportResult> ApplyAsync(Guid tenantId, int? memberId, Prepared prepared, ImportPlan plan, RemovalTargets targets,
        Guid? consumedStagingKey, CancellationToken cancellationToken) =>
        prepared.Kind == DeliveryExportKind.WorkItems
            ? WriteWorkItemsAsync(tenantId, prepared, plan, targets, consumedStagingKey, memberId, cancellationToken)
            : WriteTimeEntriesAsync(tenantId, prepared, plan, targets, consumedStagingKey, memberId, cancellationToken);

    // ---- Work items ---------------------------------------------------------

    private sealed record WorkItemRow(int Line, string? Programme, string Project, string Workstream, string Id, string Title, string RawStatus,
        WorkItemLifecycleStage Stage, string? Assignee, string? AssigneeEmail, DateTime? Due, decimal? Estimate, bool Milestone,
        string? ParentId, string RawJson);

    private static (FileImportResult?, Prepared?) PrepareWorkItems(IReadOnlyList<string[]> rows, Dictionary<string, int> header, IReadOnlyList<string> ignored)
    {
        var errors = new List<FileImportRowError>();
        var parsed = new List<WorkItemRow>();
        var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var r = 1; r < rows.Count; r++)
        {
            var cells = new RowReader(rows[r], r + 1, header, errors);
            var project = cells.Required("Project", DeliveryExportSchema.MaxNameLength);
            var id = cells.Required("WorkItemId", DeliveryExportSchema.MaxIdLength);
            var title = cells.Required("Title", DeliveryExportSchema.MaxTitleLength);
            var status = cells.Required("Status", DeliveryExportSchema.MaxStatusLength);
            var workstream = cells.Optional("Workstream", DeliveryExportSchema.MaxNameLength) ?? DefaultWorkstream;
            var assignee = cells.Optional("Assignee", DeliveryExportSchema.MaxPersonLength);
            var email = cells.Email("AssigneeEmail");
            var due = cells.Date("DueDate");
            var estimate = cells.Hours("EstimatedHours");
            var milestone = cells.Boolean("Milestone") ?? false;
            var parent = cells.Optional("ParentId", DeliveryExportSchema.MaxIdLength);
            var programmeName = cells.Optional("Programme", DeliveryExportSchema.MaxNameLength);

            if (id is not null)
            {
                if (seenIds.TryGetValue(id, out var firstLine))
                    errors.Add(new(r + 1, "WorkItemId", $"\"{id}\" already appears on row {firstLine}. Each id must be unique within the file."));
                else
                    seenIds[id] = r + 1;
            }

            if (project is null || id is null || title is null || status is null || cells.HasErrors)
                continue;

            parsed.Add(new WorkItemRow(r + 1, programmeName, project, workstream, id, title, status, ImportedStatusMapper.Map(status),
                assignee, email, due, estimate, milestone, parent, cells.ToJson()));
        }

        if (errors.Count > 0)
            return (Invalid(errors, ignored), null);
        return (null, new Prepared(DeliveryExportKind.WorkItems, ignored, parsed, [], new Dictionary<string, Guid>()));
    }

    private Task<FileImportResult> WriteWorkItemsAsync(Guid tenantId, Prepared prepared, ImportPlan plan, RemovalTargets targets,
        Guid? consumedStagingKey, int? memberId, CancellationToken cancellationToken)
    {
        var parsed = prepared.WorkItems;
        return WriteAsync(tenantId, memberId, "work items", plan, consumedStagingKey, async run =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            // Rows without a Programme share one root programme, as before
            // the column existed; a named programme is found by its name.
            var programmeKeys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            async Task<Guid> ProgrammeKeyFor(string? name)
            {
                var lookup = name ?? string.Empty;
                if (programmeKeys.TryGetValue(lookup, out var existingKey))
                    return existingKey;
                var programme = await programmes.UpsertProgrammeAsync(new Programme
                {
                    ProgrammeKey = Guid.NewGuid(), Name = name ?? "Imported delivery data", ExternalSource = SourceName,
                    ExternalId = name is null ? RootProgrammeExternalId : "programme:" + StableKey(name),
                    CreatedAtUtc = now, UpdatedAtUtc = now
                }, tenantId);
                return programmeKeys[lookup] = programme.ProgrammeKey;
            }

            var projectKeys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var workstreamKeys = new Dictionary<(string, string), Guid>();
            var unmapped = 0;

            foreach (var row in parsed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await run.TouchAsync();
                await raw.SaveAsync(tenantId, SourceName, nameof(DeliveryExportKind.WorkItems), "work-item", row.Id, row.RawJson, now);

                if (!projectKeys.TryGetValue(row.Project, out var projectKey))
                {
                    var project = await programmes.UpsertProjectAsync(new Project
                    {
                        ProjectKey = Guid.NewGuid(), ProgrammeKey = await ProgrammeKeyFor(row.Programme), Name = row.Project,
                        ExternalSource = SourceName, ExternalId = "project:" + StableKey(row.Project),
                        CreatedAtUtc = now, UpdatedAtUtc = now
                    }, tenantId);
                    projectKeys[row.Project] = projectKey = project.ProjectKey;
                }

                var workstreamId = (row.Project.ToUpperInvariant(), row.Workstream.ToUpperInvariant());
                if (!workstreamKeys.TryGetValue(workstreamId, out var workstreamKey))
                {
                    var workstream = await programmes.UpsertWorkstreamAsync(new Workstream
                    {
                        WorkstreamKey = Guid.NewGuid(), ProjectKey = projectKey, Name = row.Workstream,
                        ExternalSource = SourceName, ExternalId = "workstream:" + StableKey(row.Project + "\u001f" + row.Workstream),
                        CreatedAtUtc = now, UpdatedAtUtc = now
                    }, tenantId);
                    workstreamKeys[workstreamId] = workstreamKey = workstream.WorkstreamKey;
                }

                var staffKey = await ResolvePersonAsync(row.Assignee, row.AssigneeEmail, "imported assignee", tenantId);
                if (row.Stage == WorkItemLifecycleStage.Unmapped)
                    unmapped++;

                var item = await programmes.UpsertWorkItemAsync(new WorkItem
                {
                    WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstreamKey, Title = row.Title,
                    Stage = row.Stage, RawStatus = row.RawStatus, IsMilestone = row.Milestone,
                    AssignedStaffKey = staffKey, DueDateUtc = row.Due, EstimatedHours = row.Estimate,
                    ExternalSource = SourceName, ExternalId = row.Id, ParentExternalId = row.ParentId,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                }, tenantId);
                await programmes.UpsertWorkItemAllocationsAsync(item.WorkItemKey, staffKey is { } key ? [key] : [], now, tenantId);
            }

            var removed = targets.WorkItemKeys.Count == 0 ? 0 : await programmes.RemoveWorkItemsAsync(targets.WorkItemKeys, tenantId);

            var summary = $"{parsed.Count:N0} work items across {projectKeys.Count:N0} projects imported"
                + (unmapped > 0 ? $"; {unmapped:N0} with a status that couldn't be mapped to a stage (shown as Unmapped)" : "")
                + (removed > 0 ? $"; {removed:N0} no longer in the file removed" : "")
                + (removed > 0 && plan.HoursUnlinkedByRemoval > 0 ? $", and the {plan.HoursUnlinkedByRemoval:N1} hours recorded against them are now unlinked" : "")
                + ".";
            return (summary, parsed.Count, unmapped, removed);
        }, prepared.Ignored);
    }

    // ---- Time entries -------------------------------------------------------

    private sealed record TimeEntryRow(int Line, string? EntryId, string? WorkItemId, string? Person, string? Email,
        DateOnly Date, decimal Hours, bool? Billable, string RawJson)
    {
        /// <summary>The EntryId, or a key derived from the row's content; set once the whole file is known.</summary>
        public string ExternalId { get; init; } = EntryId ?? string.Empty;
    }

    private async Task<(FileImportResult?, Prepared?)> PrepareTimeEntriesAsync(Guid tenantId, IReadOnlyList<string[]> rows, Dictionary<string, int> header,
        IReadOnlyList<string> ignored)
    {
        var errors = new List<FileImportRowError>();
        var parsed = new List<TimeEntryRow>();
        var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var r = 1; r < rows.Count; r++)
        {
            var cells = new RowReader(rows[r], r + 1, header, errors);
            var entryId = cells.Optional("EntryId", DeliveryExportSchema.MaxIdLength);
            var workItemId = cells.Optional("WorkItemId", DeliveryExportSchema.MaxIdLength);
            var person = cells.Optional("Person", DeliveryExportSchema.MaxPersonLength);
            var email = cells.Email("PersonEmail");
            var date = cells.Date("Date", required: true);
            var hours = cells.Hours("Hours", required: true);
            var billable = cells.Boolean("Billable");

            if (hours is 0m)
                errors.Add(new(r + 1, "Hours", "Hours must be greater than zero."));

            if (entryId is not null)
            {
                if (seenIds.TryGetValue(entryId, out var firstLine))
                    errors.Add(new(r + 1, "EntryId", $"\"{entryId}\" already appears on row {firstLine}. Each id must be unique within the file."));
                else
                    seenIds[entryId] = r + 1;
            }

            if (date is null || hours is null || cells.HasErrors)
                continue;

            parsed.Add(new TimeEntryRow(r + 1, entryId, workItemId, person, email,
                DateOnly.FromDateTime(date.Value), hours.Value, billable, cells.ToJson()));
        }

        // Linking to a work item that was never imported would silently
        // orphan the hours from every per-item figure — refuse instead.
        var workItemKeys = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var row in parsed.Where(p => p.WorkItemId is not null))
        {
            if (workItemKeys.ContainsKey(row.WorkItemId!))
                continue;
            var item = await programmes.GetWorkItemByExternalIdAsync(SourceName, row.WorkItemId!, tenantId);
            if (item is null)
                errors.Add(new(row.Line, "WorkItemId", $"No imported work item has id \"{row.WorkItemId}\". Import the work items file first, or leave this blank."));
            else
                workItemKeys[row.WorkItemId!] = item.WorkItemKey;
        }

        if (errors.Count > 0)
            return (Invalid(errors, ignored), null);

        // Rows without an EntryId get a key derived from their content plus
        // an occurrence counter, so re-importing the same file changes
        // nothing and two genuinely identical entries (same person, item,
        // day, hours) still count twice. An edited row gets a new key; the
        // replacement removes its old version, so the hours aren't doubled.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var identified = parsed.Select(row =>
        {
            if (row.EntryId is not null)
                return row;
            var content = string.Join('\u001f', row.WorkItemId, row.Person?.ToUpperInvariant(), row.Email?.ToUpperInvariant(),
                row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Hours.ToString(CultureInfo.InvariantCulture));
            occurrences[content] = occurrences.GetValueOrDefault(content) + 1;
            return row with { ExternalId = DerivedIdPrefix + StableKey(content + '\u001f' + occurrences[content]) };
        }).ToList();

        // The database matches ids ignoring case, so "T-1" and "t-1" would
        // silently become one entry. Refuse rather than merge.
        var duplicate = identified.GroupBy(r => r.ExternalId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return (Invalid([new(duplicate.Skip(1).First().Line, "EntryId", $"\"{duplicate.Key}\" identifies more than one row. Each EntryId must be unique within the file.")], ignored), null);

        return (null, new Prepared(DeliveryExportKind.TimeEntries, ignored, [], identified, workItemKeys));
    }

    private Task<FileImportResult> WriteTimeEntriesAsync(Guid tenantId, Prepared prepared, ImportPlan plan, RemovalTargets targets,
        Guid? consumedStagingKey, int? memberId, CancellationToken cancellationToken)
    {
        var parsed = prepared.TimeEntries;
        var workItemKeys = prepared.WorkItemKeys;
        return WriteAsync(tenantId, memberId, "time entries", plan, consumedStagingKey, async run =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            decimal totalHours = 0, unknownBillability = 0;
            var unattributed = 0;

            foreach (var row in parsed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var externalId = row.ExternalId;
                await run.TouchAsync();
                await raw.SaveAsync(tenantId, SourceName, nameof(DeliveryExportKind.TimeEntries), "time-entry", externalId, row.RawJson, now);

                var staffKey = await ResolvePersonAsync(row.Person, row.Email, "imported time entry", tenantId);
                if (staffKey is null)
                    unattributed++;

                await programmes.UpsertTimeEntryAsync(new TimeEntry
                {
                    TimeEntryKey = Guid.NewGuid(),
                    WorkItemKey = row.WorkItemId is null ? null : workItemKeys[row.WorkItemId],
                    StaffKey = staffKey, DurationHours = row.Hours, WorkDate = row.Date,
                    IsBillable = row.Billable ?? false, BillabilityKnown = row.Billable is not null,
                    ExternalSource = SourceName, ExternalId = externalId,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                }, tenantId);

                totalHours += row.Hours;
                if (row.Billable is null)
                    unknownBillability += row.Hours;
            }

            var removed = targets.TimeEntryIds.Count == 0 ? 0
                : await programmes.DeleteTimeEntriesByExternalIdsAsync(SourceName, targets.TimeEntryIds, tenantId);

            var summary = $"{parsed.Count:N0} time entries ({totalHours:N1} hours) imported"
                + (unattributed > 0 ? $"; {unattributed:N0} not matched to a staff profile" : "")
                + (unknownBillability > 0 ? $"; {unknownBillability:N1} hours with unknown billability" : "")
                + (removed > 0 ? $"; {removed:N0} no longer in the file removed, so imported time went from {plan.HoursBefore:N1} to {plan.HoursAfter:N1} hours" : "")
                + ".";
            return (summary, parsed.Count, 0, removed);
        }, prepared.Ignored);
    }

    // ---- Shared -------------------------------------------------------------

    private async Task<FileImportResult> WriteAsync(Guid tenantId, int? memberId, string what, ImportPlan plan, Guid? consumedStagingKey,
        Func<SyncRunHandle, Task<(string Summary, int Rows, int Unmapped, int Removed)>> write, IReadOnlyList<string> ignored)
    {
        await using var run = await runs.TryBeginAsync(tenantId, SourceName, memberId);
        if (run is null)
            return FileImportResult.Rejected("Another import is already running for this organisation. Wait for it to finish, then upload again.") with { Kind = plan.Kind };

        string summary;
        int count, unmapped;
        try
        {
            // One transaction: rows, removals, the staged file's deletion, the
            // run and the audit entry commit together or not at all.
            using (var publication = scopes?.CreateScope())
            {
                await run.ReportStageAsync($"importing {what}");
                int removed;
                (summary, count, unmapped, removed) = await write(run);
                if (identities.UnresolvedCount > 0)
                    summary += identities.SuggestedCount > 0
                        ? $" {identities.UnresolvedCount:N0} people need matching on the identity queue, {identities.SuggestedCount:N0} of them with a suggested email match to approve; import again once they are approved."
                        : $" {identities.UnresolvedCount:N0} people need matching on the identity queue.";
                if (consumedStagingKey is { } stagingKey)
                    await staging.DeleteAsync(tenantId, stagingKey);

                var detail = new
                {
                    what, rows = count, unmapped, removed, rowsBefore = plan.RowsBefore, plan.Added, plan.Updated,
                    hoursBefore = plan.HoursBefore, hoursAfter = plan.HoursAfter, hoursUnlinked = plan.HoursUnlinkedByRemoval,
                    confirmedStagingKey = consumedStagingKey,
                    unresolvedPeople = identities.UnresolvedCount, suggestedPeople = identities.SuggestedCount, ambiguousPeople = identities.AmbiguousCount,
                    ignoredColumns = ignored, runKey = run.Run.RunKey
                };
                await run.CompleteAsync(summary, detail);
                await audit.LogAsync("FileImport", what, "ImportCompleted", memberId, JsonSerializer.Serialize(detail), clock.GetUtcNow().UtcDateTime, tenantId);
                publication?.Complete();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await run.FailAsync(ex);
            await audit.LogAsync("FileImport", what, "ImportFailed", memberId,
                JsonSerializer.Serialize(new { error = ex.GetType().Name, stage = run.Stage, runKey = run.Run.RunKey }),
                clock.GetUtcNow().UtcDateTime, tenantId);
            throw new InvalidOperationException($"The import stopped ({ex.Message}), and nothing was changed. Upload the file again.", ex);
        }

        // Housekeeping after the commit: a failure here must not undo it.
        if (options.Value.RawPayloadRetentionDays > 0)
            await raw.DeleteOlderThanAsync(clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RawPayloadRetentionDays));
        await staging.DeleteOlderThanAsync(clock.GetUtcNow().UtcDateTime.AddDays(-1));

        return new FileImportResult(true, summary, [], count, identities.UnresolvedCount, unmapped, ignored, Plan: plan, Kind: plan.Kind);
    }

    private Task<Guid?> ResolvePersonAsync(string? name, string? email, string context, Guid tenantId)
    {
        if (name is null && email is null)
            return Task.FromResult<Guid?>(null);

        // Files have no user ids, so the email (or, failing that, the name)
        // is the identity an Admin links on the identity queue.
        var externalUserId = (email ?? name)!.ToLowerInvariant();
        return identities.ResolveAsync(SourceName, externalUserId, email, name, context, tenantId);
    }

    private async Task<FileImportResult> RejectAsync(Guid tenantId, DeliveryExportKind kind, int? memberId, FileImportResult result)
    {
        await audit.LogAsync("FileImport", kind.ToString(), "ImportRejected", memberId,
            JsonSerializer.Serialize(new { errors = result.Errors.Count, result.Summary }), clock.GetUtcNow().UtcDateTime, tenantId);
        return result with { Kind = kind };
    }

    private static FileImportResult Invalid(List<FileImportRowError> errors, IReadOnlyList<string> ignored)
    {
        var shown = errors.OrderBy(e => e.Row).Take(MaxReportedErrors).ToList();
        var rowsAffected = errors.Select(e => e.Row).Distinct().Count();
        var more = errors.Count > shown.Count ? $" Showing the first {shown.Count}." : "";
        return FileImportResult.Rejected($"{errors.Count:N0} problems on {rowsAffected:N0} rows. Nothing was imported — fix these and upload the file again.{more}", shown, ignored);
    }

    private static Dictionary<string, int> BindHeader(string[] headerRow, IReadOnlyList<DeliveryExportColumn> columns,
        out List<FileImportRowError> errors, out IReadOnlyList<string> ignored)
    {
        errors = [];
        var bound = new Dictionary<string, int>(StringComparer.Ordinal);
        var lookup = new Dictionary<string, DeliveryExportColumn>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            lookup[DeliveryExportSchema.NormaliseHeader(column.Name)] = column;
            foreach (var alias in column.Aliases)
                lookup[DeliveryExportSchema.NormaliseHeader(alias)] = column;
        }

        var unused = new List<string>();
        for (var i = 0; i < headerRow.Length; i++)
        {
            var raw = headerRow[i].Trim();
            if (raw.Length == 0)
                continue;
            if (!lookup.TryGetValue(DeliveryExportSchema.NormaliseHeader(raw), out var column))
            {
                unused.Add(raw);
                continue;
            }
            if (bound.TryGetValue(column.Name, out var existing))
            {
                errors.Add(new(1, column.Name, $"Columns \"{headerRow[existing].Trim()}\" and \"{raw}\" both mean {column.Name}. Remove or rename one."));
                continue;
            }
            bound[column.Name] = i;
        }

        foreach (var missing in columns.Where(c => c.Required && !bound.ContainsKey(c.Name)))
            errors.Add(new(1, missing.Name, $"Required column {missing.Name} is missing. Accepted headers: {string.Join(", ", new[] { missing.Name }.Concat(missing.Aliases))}."));

        ignored = unused;
        return bound;
    }

    /// <summary>A short stable key for a name-derived external id — names can exceed the 128-character id column.</summary>
    private static string StableKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))[..32].ToLowerInvariant();

    /// <summary>Reads and validates the cells of one row, recording every problem rather than stopping at the first.</summary>
    private sealed class RowReader(string[] cells, int line, Dictionary<string, int> header, List<FileImportRowError> errors)
    {
        private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"];
        private readonly int _errorsAtStart = errors.Count;

        public bool HasErrors => errors.Count > _errorsAtStart;

        private string? Cell(string column)
        {
            if (!header.TryGetValue(column, out var index) || index >= cells.Length)
                return null;
            var value = cells[index].Trim();
            return value.Length == 0 ? null : value;
        }

        public string? Required(string column, int maxLength)
        {
            var value = Cell(column);
            if (value is null)
            {
                errors.Add(new(line, column, $"{column} is required."));
                return null;
            }
            return CheckLength(column, value, maxLength);
        }

        public string? Optional(string column, int maxLength)
        {
            var value = Cell(column);
            return value is null ? null : CheckLength(column, value, maxLength);
        }

        public string? Email(string column)
        {
            var value = Optional(column, 256);
            if (value is null)
                return null;
            var at = value.IndexOf('@');
            if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1 || value.Any(char.IsWhiteSpace))
            {
                errors.Add(new(line, column, $"\"{value}\" is not an email address."));
                return null;
            }
            return value;
        }

        public DateTime? Date(string column, bool required = false)
        {
            var value = Cell(column);
            if (value is null)
            {
                if (required)
                    errors.Add(new(line, column, $"{column} is required."));
                return null;
            }
            if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
            errors.Add(new(line, column, $"\"{value}\" is not a date. Use yyyy-MM-dd or dd/MM/yyyy."));
            return null;
        }

        public decimal? Hours(string column, bool required = false)
        {
            var value = Cell(column);
            if (value is null)
            {
                if (required)
                    errors.Add(new(line, column, $"{column} is required."));
                return null;
            }

            decimal hours;
            if (value.Contains(':'))
            {
                // "1:30" or "01:30:00" — the duration format Toggl/Clockify export.
                var parts = value.Split(':');
                if (parts.Length is < 2 or > 3 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    || int.Parse(parts[1], CultureInfo.InvariantCulture) >= 60 || (parts.Length == 3 && int.Parse(parts[2], CultureInfo.InvariantCulture) >= 60))
                {
                    errors.Add(new(line, column, $"\"{value}\" is not a duration. Use decimal hours (1.5) or h:mm (1:30)."));
                    return null;
                }
                hours = int.Parse(parts[0], CultureInfo.InvariantCulture) + int.Parse(parts[1], CultureInfo.InvariantCulture) / 60m
                    + (parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) / 3600m : 0m);
                hours = Math.Round(hours, 4);
            }
            else if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out hours))
            {
                errors.Add(new(line, column, $"\"{value}\" is not a number of hours. Use a plain decimal like 1.5."));
                return null;
            }

            if (hours > 10_000m)
            {
                errors.Add(new(line, column, $"{hours} hours is implausibly large for one row."));
                return null;
            }
            return hours;
        }

        public bool? Boolean(string column)
        {
            var value = Cell(column);
            if (value is null)
                return null;
            switch (value.ToLowerInvariant())
            {
                case "yes" or "y" or "true" or "1": return true;
                case "no" or "n" or "false" or "0": return false;
                default:
                    errors.Add(new(line, column, $"\"{value}\" should be yes or no."));
                    return null;
            }
        }

        public string ToJson() =>
            JsonSerializer.Serialize(header.ToDictionary(h => h.Key, h => h.Value < cells.Length ? cells[h.Value] : null));

        private string? CheckLength(string column, string value, int maxLength)
        {
            if (value.Length <= maxLength)
                return value;
            errors.Add(new(line, column, $"{column} is {value.Length} characters; the limit is {maxLength}."));
            return null;
        }
    }
}
