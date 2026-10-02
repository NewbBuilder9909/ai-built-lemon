using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Silver-layer persistence for the whole Programme/Project/Workstream/
/// WorkItem/Dependency/Risk/Issue set. One repository covering all of them
/// (rather than one per entity) because they're structurally similar and
/// always read together for the Gold overview — split it later only if a
/// single entity's query needs genuinely diverge.
///
/// UpsertXAsync methods match on (TenantId, ExternalSource, ExternalId) when
/// present so re-running a ClickUp sync updates existing rows instead of
/// duplicating them, and so the same external id in two tenants never
/// collides; when ExternalId is null (e.g. rows created some other way in
/// future) they always insert. Every method takes an explicit tenantId
/// rather than trusting the domain object's own (nullable) TenantId, same
/// convention as IBrandingRepository — the filter is always in the query,
/// never applied afterward in memory. See docs/tenancy.md.
///
/// Reads live on IProgrammeReadRepository, which this extends; take that
/// instead unless the caller writes (Architecture/ProgrammeRepositoryWriterTests).
/// </summary>
public interface IProgrammeRepository : IProgrammeReadRepository
{
    /// <summary>Same (TenantId, ExternalSource, ExternalId) upsert semantics as UpsertWorkItemAsync.</summary>
    Task<PlannedAllocation> UpsertPlannedAllocationAsync(PlannedAllocation allocation, Guid tenantId);

    Task<Programme> UpsertProgrammeAsync(Programme programme, Guid tenantId);

    Task<Project> UpsertProjectAsync(Project project, Guid tenantId);

    Task<Workstream> UpsertWorkstreamAsync(Workstream workstream, Guid tenantId);

    Task<WorkItem> UpsertWorkItemAsync(WorkItem workItem, Guid tenantId);

    /// <summary>
    /// Full delete-then-reinsert of every allocation row for this work item —
    /// same replace-not-patch semantics UpsertWorkItemAsync already applies
    /// to a WorkItem's own columns. The first key in staffKeys becomes
    /// IsPrimary; the caller (ClickUpMappingService) is responsible for
    /// ordering it that way.
    /// </summary>
    Task UpsertWorkItemAllocationsAsync(Guid workItemKey, IReadOnlyList<Guid> staffKeys, DateTime nowUtc, Guid tenantId);

    /// <summary>
    /// Many work items and their allocations in one transaction, with the
    /// same per-row semantics as <see cref="UpsertWorkItemAsync"/> followed by
    /// <see cref="UpsertWorkItemAllocationsAsync"/>, applied in input order.
    /// Sync writes a page at a time through this (finding A3: a task used to
    /// cost about six round trips). The SQL repository runs it as one
    /// transaction, all or nothing: a workstream that is not
    /// this tenant's fails the whole batch before anything is written.
    /// </summary>
    async Task<IReadOnlyList<WorkItem>> UpsertWorkItemsAsync(IReadOnlyList<WorkItemUpsert> items, DateTime nowUtc, Guid tenantId)
    {
        var saved = new List<WorkItem>(items.Count);
        foreach (var upsert in items)
        {
            var workItem = await UpsertWorkItemAsync(upsert.Item, tenantId);
            await UpsertWorkItemAllocationsAsync(workItem.WorkItemKey, upsert.AllocatedStaffKeys, nowUtc, tenantId);
            saved.Add(workItem);
        }

        return saved;
    }

    /// <summary>
    /// Many time entries, with the per-row semantics of
    /// <see cref="UpsertTimeEntryAsync"/> in input order, written by the SQL
    /// repository as one transaction, as
    /// for <see cref="UpsertWorkItemsAsync"/>.
    /// </summary>
    async Task<IReadOnlyList<TimeEntry>> UpsertTimeEntriesAsync(IReadOnlyList<TimeEntry> entries, Guid tenantId)
    {
        var saved = new List<TimeEntry>(entries.Count);
        foreach (var entry in entries)
        {
            saved.Add(await UpsertTimeEntryAsync(entry, tenantId));
        }

        return saved;
    }

    Task<Dependency> UpsertDependencyAsync(Dependency dependency, Guid tenantId);

    Task<Risk> UpsertRiskAsync(Risk risk, Guid tenantId);

    Task<Issue> UpsertIssueAsync(Issue issue, Guid tenantId);

    Task<TimeEntry> UpsertTimeEntryAsync(TimeEntry timeEntry, Guid tenantId);

    /// <summary>Remove only an explicitly identified source record, never infer deletion from an absent result.</summary>
    Task<int> DeleteTimeEntryByExternalIdAsync(string source, string externalId, Guid tenantId);

    /// <summary>
    /// Removes the named rows of one source. For a replacement import, whose
    /// new file is the whole truth for that source, after the person
    /// importing has confirmed the removals. Returns how many rows went.
    /// </summary>
    Task<int> DeleteTimeEntriesByExternalIdsAsync(string source, IReadOnlyCollection<string> externalIds, Guid tenantId);

    /// <summary>
    /// Removes work items with their allocations, dependencies (either end)
    /// and estimate baselines. Time recorded against them is <b>unlinked,
    /// not deleted</b>: hours are never lost because the item they were
    /// logged against went. Returns how many work items were removed.
    /// </summary>
    Task<int> RemoveWorkItemsAsync(IReadOnlyCollection<Guid> workItemKeys, Guid tenantId);

    Task<Customer> UpsertCustomerAsync(Customer customer, Guid tenantId);

    Task<Programme> AssignProgrammeToCustomerAsync(Guid programmeKey, Guid? customerKey, Guid tenantId);

    Task<Programme> SetProgrammeBudgetAsync(Guid programmeKey, decimal? budgetAmount, string? budgetCurrency, Guid tenantId);

    /// <summary>No-op (returns the existing row) if a baseline is already locked for this workstream — a baseline locks once.</summary>
    Task<WorkstreamBaseline> LockBaselineAsync(Guid workstreamKey, decimal baselineHours, Guid? lockedByStaffKey, DateTime nowUtc, Guid tenantId);

    Task<ChangeRequest> CreateChangeRequestAsync(ChangeRequest changeRequest, Guid tenantId);

    Task<ChangeRequest> DecideChangeRequestAsync(Guid changeRequestKey, ChangeRequestStatus status, Guid? decidedByStaffKey, DateTime nowUtc, Guid tenantId);

    Task<ProgrammeStakeholder> AddStakeholderAsync(ProgrammeStakeholder stakeholder, Guid tenantId);

    Task RemoveStakeholderAsync(Guid programmeStakeholderKey, Guid tenantId);

    Task<ReportingSnapshot> CaptureReportingSnapshotAsync(ReportingSnapshot snapshot, Guid tenantId);

    Task<Alert> RaiseAlertAsync(Alert alert, Guid tenantId);

    Task AcknowledgeAlertAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId);
}

/// <summary>A work item and the people allocated to it, first one primary, for a batched upsert.</summary>
public sealed record WorkItemUpsert(WorkItem Item, IReadOnlyList<Guid> AllocatedStaffKeys);
