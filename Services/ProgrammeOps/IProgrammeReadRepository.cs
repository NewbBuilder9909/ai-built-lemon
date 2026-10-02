using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// The read half of <see cref="IProgrammeRepository"/>. Gold query services
/// depend on this, not on the full repository, so a report structurally
/// cannot write Silver rows (Architecture/ProgrammeRepositoryWriterTests). Every
/// method takes an explicit tenantId, as on the write side.
///
/// Methods with a body are bounded reads added in Phase 2 of
/// docs/architecture-review-2026-09-24.md. The body is the definition, written
/// over the plain list reads, and is what an in-memory fake uses; the SQL
/// repository overrides each with a seek or an aggregate, and
/// Integration/BoundedReadIntegrationTests proves the two agree.
///
/// Every read ends with a <see cref="CancellationToken"/> (finding B6), so a
/// report the caller has abandoned stops at the database instead of running
/// to completion. Architecture/CancellationTokenTests keeps it that way.
/// </summary>
public interface IProgrammeReadRepository
{
    Task<IReadOnlyList<Programme>> GetProgrammesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> GetProjectsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Workstream>> GetWorkstreamsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Dependency>> GetDependenciesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Risk>> GetRisksAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Issue>> GetIssuesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entries whose <see cref="TimeEntry.ReportDate"/> falls in [from, to],
    /// inclusive. A period report's cost is then bounded by the period it
    /// shows, not by how much history the tenant has (finding A1). The
    /// default filters the full read; the SQL repository seeks an index.
    /// </summary>
    async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        (await GetTimeEntriesAsync(tenantId, cancellationToken))
            .Where(entry => entry.ReportDate is { } date && date >= from && date <= to)
            .ToList();

    /// <summary>
    /// Total hours ever logged against each work item, summed in the
    /// database. Effort variance is cumulative by definition, so this is the
    /// one report input that must span all history; it returns one number per
    /// work item instead of every entry. Unlinked time is not included. With
    /// <paramref name="staffKey"/>, only that person's hours (My Work).
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, decimal>> GetLoggedHoursByWorkItemAsync(Guid tenantId, Guid? staffKey = null, CancellationToken cancellationToken = default) =>
        (await GetTimeEntriesAsync(tenantId, cancellationToken))
            .Where(entry => entry.WorkItemKey is not null && (staffKey is null || entry.StaffKey == staffKey))
            .GroupBy(entry => entry.WorkItemKey!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.DurationHours));

    /// <summary>
    /// Distinct projects per person per ISO week, over every dated entry
    /// with a person. Delivery Load's baseline is "the last N weeks that have
    /// data", which has no fixed lookback, so the series is all history; this
    /// returns it as one row per person-week instead of every entry. A work
    /// item counts only through a workstream of the same tenant.
    /// </summary>
    async Task<IReadOnlyList<WeeklyProjectCount>> GetWeeklyProjectCountsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var projectByWorkstream = (await GetWorkstreamsAsync(tenantId, cancellationToken)).ToDictionary(w => w.WorkstreamKey, w => w.ProjectKey);
        var projectByWorkItem = (await GetWorkItemsAsync(tenantId, cancellationToken))
            .Where(i => projectByWorkstream.ContainsKey(i.WorkstreamKey))
            .ToDictionary(i => i.WorkItemKey, i => projectByWorkstream[i.WorkstreamKey]);
        return (await GetTimeEntriesAsync(tenantId, cancellationToken))
            .Where(t => t.WorkDate is not null && t.StaffKey is not null)
            .GroupBy(t => (Staff: t.StaffKey!.Value, Week: WeeklyProjectCount.WeekStartOf(t.WorkDate!.Value)))
            .Select(g => new WeeklyProjectCount(g.Key.Staff, g.Key.Week, g
                .Where(t => t.WorkItemKey is { } key && projectByWorkItem.ContainsKey(key))
                .Select(t => projectByWorkItem[t.WorkItemKey!.Value])
                .Distinct()
                .Count()))
            .ToList();
    }

    /// <summary>The non-blank sources of every dated entry that has a person.</summary>
    async Task<IReadOnlyList<string>> GetDatedTimeSourcesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await GetTimeEntriesAsync(tenantId, cancellationToken))
            .Where(t => t.WorkDate is not null && t.StaffKey is not null && !string.IsNullOrWhiteSpace(t.ExternalSource))
            .Select(t => t.ExternalSource!)
            .Distinct()
            .ToList();

    /// <summary>One work item by key, or null if it is not this tenant's (finding A4: never load the list to find one row).</summary>
    async Task<WorkItem?> GetWorkItemByKeyAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        (await GetWorkItemsAsync(tenantId, cancellationToken)).FirstOrDefault(item => item.WorkItemKey == workItemKey);

    /// <summary>
    /// Search's read: work items whose title or source id contains
    /// <paramref name="term"/>, ignoring case, ordered by title then key, at
    /// most <paramref name="take"/>. Never the list.
    /// </summary>
    async Task<IReadOnlyList<WorkItem>> SearchWorkItemsAsync(Guid tenantId, string term, int take, CancellationToken cancellationToken = default) =>
        (await GetWorkItemsAsync(tenantId, cancellationToken))
            .Where(item => item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (item.ExternalId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.WorkItemKey)
            .Take(take)
            .ToList();

    /// <summary>One risk by key, or null if it is not this tenant's.</summary>
    async Task<Risk?> GetRiskByKeyAsync(Guid riskKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        (await GetRisksAsync(tenantId, cancellationToken)).FirstOrDefault(risk => risk.RiskKey == riskKey);

    /// <summary>One issue by key, or null if it is not this tenant's.</summary>
    async Task<Issue?> GetIssueByKeyAsync(Guid issueKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        (await GetIssuesAsync(tenantId, cancellationToken)).FirstOrDefault(issue => issue.IssueKey == issueKey);

    /// <summary>Every entry logged against one work item.</summary>
    async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesForWorkItemAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        (await GetTimeEntriesAsync(tenantId, cancellationToken)).Where(entry => entry.WorkItemKey == workItemKey).ToList();

    /// <summary>
    /// Every time entry from one source. For reconciling that source's own
    /// rows (a replacement import comparing old against new), never for
    /// showing a period, which must use the period read.
    /// </summary>
    async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesBySourceAsync(Guid tenantId, string source, CancellationToken cancellationToken = default) =>
        (await GetTimeEntriesAsync(tenantId, cancellationToken)).Where(entry => entry.ExternalSource == source).ToList();

    /// <summary>
    /// Whether the tenant holds any work items or time entries from one
    /// source: what decides whether a source-specific report exists for it.
    /// </summary>
    async Task<SourceDataPresence> GetSourcePresenceAsync(Guid tenantId, string source, CancellationToken cancellationToken = default) =>
        new((await GetWorkItemsAsync(tenantId, cancellationToken)).Any(item => item.ExternalSource == source),
            (await GetTimeEntriesAsync(tenantId, cancellationToken)).Any(entry => entry.ExternalSource == source));

    /// <summary>Tenant-wide totals a period report states beside its rows.</summary>
    async Task<TimeEntryCoverage> GetTimeEntryCoverageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entries = await GetTimeEntriesAsync(tenantId, cancellationToken);
        return new TimeEntryCoverage(
            entries.Where(entry => entry.ReportDate is null).Sum(entry => entry.DurationHours),
            entries.Any(entry => entry.ExternalSource == "Tempo"));
    }

    Task<IReadOnlyList<Customer>> GetCustomersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkstreamBaseline>> GetWorkstreamBaselinesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChangeRequest>> GetChangeRequestsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProgrammeStakeholder>> GetStakeholdersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportingSnapshot>> GetReportingSnapshotsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Alert>> GetOpenAlertsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>One page of open alerts, newest first.</summary>
    async Task<ResultPage<Alert>> GetOpenAlertsPageAsync(Guid tenantId, PageRequest page, IReadOnlyCollection<Guid>? visibleStaffKeys = null, CancellationToken cancellationToken = default) =>
        ResultPage<Alert>.Of((await GetOpenAlertsAsync(tenantId, cancellationToken)).Where(a => visibleStaffKeys is null || a.Type == AlertType.WorkstreamBlocked || visibleStaffKeys.Contains(a.EntityKey)).ToList(), page);

    /// <summary>How many alerts are open, for a badge, without reading them.</summary>
    async Task<int> CountOpenAlertsAsync(Guid tenantId, IReadOnlyCollection<Guid>? visibleStaffKeys = null, CancellationToken cancellationToken = default) =>
        (await GetOpenAlertsAsync(tenantId, cancellationToken)).Count(a => visibleStaffKeys is null || a.Type == AlertType.WorkstreamBlocked || visibleStaffKeys.Contains(a.EntityKey));

    Task<WorkItem?> GetWorkItemByExternalIdAsync(string externalSource, string externalId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Work item keys for many external ids of one source, in one read.
    /// Ids with no work item in this tenant are absent from the result.
    /// </summary>
    async Task<IReadOnlyDictionary<string, Guid>> GetWorkItemKeysByExternalIdAsync(string externalSource, IReadOnlyCollection<string> externalIds, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var keys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalId in externalIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await GetWorkItemByExternalIdAsync(externalSource, externalId, tenantId, cancellationToken) is { } item)
            {
                keys[externalId] = item.WorkItemKey;
            }
        }

        return keys;
    }

    /// <summary>Planned (scheduled) time — never counted as delivery; see Models/Programme/PlannedAllocation.</summary>
    Task<IReadOnlyList<PlannedAllocation>> GetPlannedAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsByWorkItemKeyAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default);
}
