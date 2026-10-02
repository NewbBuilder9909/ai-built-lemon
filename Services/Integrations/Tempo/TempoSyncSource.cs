using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Integrations.OAuth;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Integrations.Tempo;

public sealed class TempoSyncSource(
    TempoApiClient api,
    ConnectorAccessTokenService tokens,
    ISourceConnectionRepository connections,
    ISourceCredentialProtector protector,
    IRawConnectorPayloadRepository raw,
    IProgrammeRepository programmes,
    IStaffIdentityResolver identities,
    IAuditLogRepository audit,
    SyncRunCoordinator runs,
    IOptions<ProgrammeOpsOptions> options,
    TimeProvider clock,
    IOptions<TempoReconciliationOptions> reconciliation,
    IScopeProvider scopes,
    ILogger<TempoSyncSource> logger) : ISyncSource
{
    public string Name => "Tempo";
    public string DisplayName => "Tempo Timesheets";
    public string FeatureKey => ProductFeature.TempoSync;
    public SourceCapabilities Capabilities => SourceCapabilities.Time;

    public async Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var credential = await tokens.GetAsync(tenantId, Name, cancellationToken);
        var tempoConnection = await connections.GetActiveForTenantAsync(tenantId, Name);
        if (tempoConnection is null || protector.Unprotect(tempoConnection.ProtectedCredentialJson) != credential)
            throw new InvalidOperationException("Tempo connection changed. Retry the sync.");
        var jiraConnection = await connections.GetActiveForTenantAsync(tenantId, "Jira");
        var jira = protector.Unprotect(jiraConnection?.ProtectedCredentialJson);
        if (jira?.SiteUrl is null || !string.Equals(jira.SiteUrl, credential.SiteUrl, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(jira.WorkspaceId, out var cloudId) || cloudId == Guid.Empty)
            throw new InvalidOperationException("Tempo must be paired with the same connected Jira Cloud site.");

        var auditEnabled = reconciliation.Value.IsEnabled(tenantId);
        DateOnly? auditFrom = auditEnabled
            ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-reconciliation.Value.DeletionLookbackDays) : null;
        await using var run = await runs.TryBeginAsync(tenantId, Name, triggeredByMemberId)
            ?? throw new InvalidOperationException("A Tempo sync is already running for this tenant.");
        string summary;
        try
        {
            await run.ReportStageAsync("fetching Tempo worklogs");
            var worklogs = await api.GetWorklogsAsync(credential.ApiToken, cancellationToken: cancellationToken);
            IReadOnlyList<JsonElement> deletions = [];
            if (auditFrom is { } from)
            {
                await run.ReportStageAsync("fetching Tempo deletion audit");
                // Fetch every audit page before any Silver mutation. No inference from missing worklogs.
                deletions = await api.GetDeletedWorklogsAsync(credential.ApiToken, from, cancellationToken);
            }
            var observedAt = clock.GetUtcNow();
            var batch = TempoReconciliationBatch.Build(worklogs, deletions, auditFrom, observedAt);
            foreach (var worklog in batch.Worklogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await run.TouchAsync();
                await raw.SaveAsync(tenantId, Name, cloudId.ToString("D"), "worklog", Id(worklog.Fact.Id),
                    worklog.Payload.GetRawText(), observedAt.UtcDateTime);
            }
            foreach (var deletion in batch.Deletions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await run.TouchAsync();
                await raw.SaveAsync(tenantId, Name, cloudId.ToString("D"), "worklog-deletion", Id(deletion.Id),
                    deletion.Payload.GetRawText(), observedAt.UtcDateTime);
            }

            // Lease renewal locks its database row until commit. Time facts, removals, audit and run status publish together.
            using var publication = scopes.CreateScope(isolationLevel: IsolationLevel.Serializable);
            await run.ReportStageAsync("publishing Tempo reconciliation");
            await RequireUnchangedAsync(tenantId, tempoConnection);
            await RequireUnchangedAsync(tenantId, jiraConnection!);
            var imported = 0;
            var unlinked = 0;
            var unresolved = 0;
            var removed = 0;
            decimal hours = 0, unlinkedHours = 0, unresolvedHours = 0;
            foreach (var deletion in batch.Deletions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                removed += await programmes.DeleteTimeEntryByExternalIdAsync(Name, $"{cloudId:D}:{Id(deletion.Id)}", tenantId);
            }
            foreach (var worklog in batch.Worklogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fact = worklog.Fact;
                // Same form as the Jira work item's ExternalId, kept even when that
                // issue isn't synced, so unlinked hours can name their issue.
                var issueExternalId = fact.IssueId is null ? null : $"{cloudId:D}:{Id(fact.IssueId.Value)}";
                var item = issueExternalId is null ? null : await programmes.GetWorkItemByExternalIdAsync(
                    "Jira", issueExternalId, tenantId);
                var staff = fact.AccountId is null ? null : await identities.ResolveAsync(
                    "Jira", $"{cloudId:D}:{fact.AccountId}", null, null, "Tempo worklog author", tenantId);
                var duration = fact.Seconds / 3600m;
                if (item is null) { unlinked++; unlinkedHours += duration; }
                if (staff is null) { unresolved++; unresolvedHours += duration; }
                hours += duration;
                await programmes.UpsertTimeEntryAsync(new TimeEntry
                {
                    TimeEntryKey = Guid.NewGuid(), WorkItemKey = item?.WorkItemKey, StaffKey = staff,
                    DurationHours = duration, WorkDate = fact.WorkDate, StartedAtUtc = null,
                    IsBillable = false, BillabilityKnown = false, ExternalSource = Name,
                    ExternalId = $"{cloudId:D}:{Id(fact.Id)}", SourceWorkItemExternalId = issueExternalId, CreatedAtUtc = observedAt.UtcDateTime, UpdatedAtUtc = observedAt.UtcDateTime
                }, tenantId);
                imported++;
            }
            var coverage = auditFrom is { } start
                ? $"Deletion audit from {start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} UTC; {removed} stored rows removed. Earlier history unverified."
                : "Deletion audit disabled; retained history unverified.";
            summary = $"Tempo: {imported} worklogs; {unlinked} unlinked issues; {unresolved} unmapped worklogs. {coverage}";
            var detail = new
            {
                mappingVersion = "tempo-reconciliation-v1", cloudId, connectionKey = tempoConnection.ConnectionKey,
                worklogs = imported, hours, unlinkedIssues = unlinked, unlinkedHours, unmappedWorklogs = unresolved, unmappedHours = unresolvedHours,
                duplicateWorklogs = batch.DuplicateWorklogs, duplicateDeletions = batch.DuplicateDeletions,
                deletionEvents = batch.Deletions.Count, removedWorklogs = removed, suppressedWorklogs = batch.SuppressedWorklogs,
                deletionCoverage = auditEnabled ? "bounded-audit-window" : "unavailable", auditFrom,
                auditObservedAtUtc = auditEnabled ? (DateTime?)observedAt.UtcDateTime : null,
                completeHistoricalCoverage = false, billability = "unknown", approval = "unknown", runKey = run.Run.RunKey
            };
            await audit.LogAsync("TempoSync", cloudId.ToString("D"), "SyncCompleted", triggeredByMemberId,
                JsonSerializer.Serialize(detail), observedAt.UtcDateTime, tenantId);
            cancellationToken.ThrowIfCancellationRequested();
            await run.CompleteAsync(summary, detail);
            publication.Complete();
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await audit.LogAsync("TempoSync", cloudId.ToString("D"), "SyncFailed", triggeredByMemberId,
                JsonSerializer.Serialize(new { error = ex.GetType().Name, stage = run.Stage, runKey = run.Run.RunKey }),
                clock.GetUtcNow().UtcDateTime, tenantId);
            throw;
        }
        // Maintenance failure must not turn an already committed publication into a reported failed sync.
        try
        {
            if (options.Value.RawPayloadRetentionDays > 0)
                await raw.DeleteOlderThanAsync(clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RawPayloadRetentionDays));
        }
        catch (Exception error)
        {
            logger.LogWarning("Tempo raw retention failed for tenant {TenantId}: {ErrorType}", tenantId, error.GetType().Name);
        }
        return new SyncOutcome(summary, identities.UnresolvedCount);
    }

    private async Task RequireUnchangedAsync(Guid tenantId, SourceConnection expected)
    {
        var current = await connections.GetActiveForTenantAsync(tenantId, expected.Source);
        if (current?.ConnectionKey != expected.ConnectionKey || current.ProtectedCredentialJson != expected.ProtectedCredentialJson)
            throw new InvalidOperationException("The paired source connection changed during extraction. Retry the sync.");
    }

    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
}
