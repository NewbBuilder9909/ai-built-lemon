using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// In-memory stand-in for IProgrammeRepository, following the hand-rolled
/// fake convention used elsewhere in this test project (see
/// Staff/LeaveApprovalServiceTests) rather than a mocking library. Upsert
/// matches on (TenantId, ExternalSource, ExternalId) exactly like the real
/// NPoco repository, so mapping-service tests can assert re-sync behaviour.
/// Every Get*Async method filters by tenantId and every Upsert*/Create*/
/// Assign*/Set*/Lock*/Add*/Capture*/Raise* method stamps TenantId = tenantId
/// on the record before storing, mirroring FakeBrandingRepository.
/// </summary>
public sealed class FakeProgrammeRepository : IProgrammeRepository
{
    public Task<int> DeleteTimeEntryByExternalIdAsync(string source, string externalId, Guid tenantId) =>
        Task.FromResult(TimeEntries.RemoveAll(t => t.TenantId == tenantId && t.ExternalSource == source && t.ExternalId == externalId));

    public Task<int> DeleteTimeEntriesByExternalIdsAsync(string source, IReadOnlyCollection<string> externalIds, Guid tenantId) =>
        Task.FromResult(TimeEntries.RemoveAll(t => t.TenantId == tenantId && t.ExternalSource == source && externalIds.Contains(t.ExternalId!)));

    public Task<int> RemoveWorkItemsAsync(IReadOnlyCollection<Guid> workItemKeys, Guid tenantId)
    {
        for (var i = 0; i < TimeEntries.Count; i++)
            if (TimeEntries[i].TenantId == tenantId && TimeEntries[i].WorkItemKey is { } key && workItemKeys.Contains(key))
                TimeEntries[i] = TimeEntries[i] with { WorkItemKey = null };
        Allocations.RemoveAll(a => workItemKeys.Contains(a.WorkItemKey));
        Dependencies.RemoveAll(d => workItemKeys.Contains(d.WorkItemKey) || workItemKeys.Contains(d.DependsOnWorkItemKey));
        return Task.FromResult(WorkItems.RemoveAll(w => w.TenantId == tenantId && workItemKeys.Contains(w.WorkItemKey)));
    }

    public readonly List<Programme> Programmes = [];
    public readonly List<Project> Projects = [];
    public readonly List<Workstream> Workstreams = [];
    public readonly List<WorkItem> WorkItems = [];
    public readonly List<Dependency> Dependencies = [];
    public readonly List<Risk> Risks = [];
    public readonly List<Issue> Issues = [];
    public readonly List<TimeEntry> TimeEntries = [];
    public readonly List<Customer> Customers = [];
    public readonly List<WorkstreamBaseline> WorkstreamBaselines = [];
    public readonly List<ChangeRequest> ChangeRequests = [];
    public readonly List<ProgrammeStakeholder> Stakeholders = [];
    public readonly List<ReportingSnapshot> ReportingSnapshots = [];
    public readonly List<Alert> Alerts = [];
    public readonly List<WorkItemAllocation> Allocations = [];
    public readonly List<PlannedAllocation> PlannedAllocations = [];

    public Task<IReadOnlyList<PlannedAllocation>> GetPlannedAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlannedAllocation>>(PlannedAllocations.Where(p => p.TenantId == tenantId).ToList());

    public Task<PlannedAllocation> UpsertPlannedAllocationAsync(PlannedAllocation allocation, Guid tenantId)
    {
        var existing = PlannedAllocations.FirstOrDefault(p =>
            p.TenantId == tenantId && Matches(p.ExternalSource, p.ExternalId, allocation.ExternalSource, allocation.ExternalId));
        if (existing is not null)
        {
            PlannedAllocations.Remove(existing);
            allocation = allocation with { PlannedAllocationKey = existing.PlannedAllocationKey };
        }

        allocation = allocation with { TenantId = tenantId };
        PlannedAllocations.Add(allocation);
        return Task.FromResult(allocation);
    }

    public Task<IReadOnlyList<Programme>> GetProgrammesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Programme>>(Programmes.Where(p => p.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Project>> GetProjectsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Project>>(Projects.Where(p => p.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Workstream>> GetWorkstreamsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Workstream>>(Workstreams.Where(w => w.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkItem>>(WorkItems.Where(w => w.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Dependency>> GetDependenciesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Dependency>>(Dependencies.Where(d => d.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Risk>> GetRisksAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Risk>>(Risks.Where(r => r.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Issue>> GetIssuesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Issue>>(Issues.Where(i => i.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(TimeEntries.Where(t => t.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<Customer>> GetCustomersAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Customer>>(Customers.Where(c => c.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<WorkstreamBaseline>> GetWorkstreamBaselinesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkstreamBaseline>>(WorkstreamBaselines.Where(b => b.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ChangeRequest>> GetChangeRequestsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChangeRequest>>(ChangeRequests.Where(c => c.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ProgrammeStakeholder>> GetStakeholdersAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgrammeStakeholder>>(Stakeholders.Where(s => s.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<ReportingSnapshot>> GetReportingSnapshotsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReportingSnapshot>>(
            ReportingSnapshots.Where(s => s.TenantId == tenantId).OrderBy(s => s.CapturedAtUtc).ToList());

    public Task<IReadOnlyList<Alert>> GetOpenAlertsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Alert>>(
            Alerts.Where(a => a.TenantId == tenantId && a.AcknowledgedAtUtc is null).OrderByDescending(a => a.RaisedAtUtc).ToList());

    public Task<WorkItem?> GetWorkItemByExternalIdAsync(string externalSource, string externalId, Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkItems.FirstOrDefault(w => w.TenantId == tenantId && Matches(w.ExternalSource, w.ExternalId, externalSource, externalId)));

    public Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkItemAllocation>>(Allocations.Where(a => a.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<WorkItemAllocation>> GetAllocationsByWorkItemKeyAsync(Guid workItemKey, Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkItemAllocation>>(
            Allocations.Where(a => a.WorkItemKey == workItemKey && a.TenantId == tenantId).ToList());

    public Task<Programme> UpsertProgrammeAsync(Programme programme, Guid tenantId)
    {
        var existing = Programmes.FirstOrDefault(p =>
            p.TenantId == tenantId && Matches(p.ExternalSource, p.ExternalId, programme.ExternalSource, programme.ExternalId));
        if (existing is not null)
        {
            Programmes.Remove(existing);
            programme = programme with { ProgrammeKey = existing.ProgrammeKey };
        }

        programme = programme with { TenantId = tenantId };
        Programmes.Add(programme);
        return Task.FromResult(programme);
    }

    public Task<Project> UpsertProjectAsync(Project project, Guid tenantId)
    {
        var existing = Projects.FirstOrDefault(p =>
            p.TenantId == tenantId && Matches(p.ExternalSource, p.ExternalId, project.ExternalSource, project.ExternalId));
        if (existing is not null)
        {
            Projects.Remove(existing);
            project = project with { ProjectKey = existing.ProjectKey };
        }

        project = project with { TenantId = tenantId };
        Projects.Add(project);
        return Task.FromResult(project);
    }

    public Task<Workstream> UpsertWorkstreamAsync(Workstream workstream, Guid tenantId)
    {
        var existing = Workstreams.FirstOrDefault(w =>
            w.TenantId == tenantId && Matches(w.ExternalSource, w.ExternalId, workstream.ExternalSource, workstream.ExternalId));
        if (existing is not null)
        {
            Workstreams.Remove(existing);
            workstream = workstream with { WorkstreamKey = existing.WorkstreamKey };
        }

        workstream = workstream with { TenantId = tenantId };
        Workstreams.Add(workstream);
        return Task.FromResult(workstream);
    }

    public Task<WorkItem> UpsertWorkItemAsync(WorkItem workItem, Guid tenantId)
    {
        var existing = WorkItems.FirstOrDefault(w =>
            w.TenantId == tenantId && Matches(w.ExternalSource, w.ExternalId, workItem.ExternalSource, workItem.ExternalId));
        if (existing is not null)
        {
            WorkItems.Remove(existing);
            workItem = workItem with { WorkItemKey = existing.WorkItemKey };
        }

        workItem = workItem with { TenantId = tenantId };
        WorkItems.Add(workItem);
        return Task.FromResult(workItem);
    }

    public Task UpsertWorkItemAllocationsAsync(Guid workItemKey, IReadOnlyList<Guid> staffKeys, DateTime nowUtc, Guid tenantId)
    {
        Allocations.RemoveAll(a => a.WorkItemKey == workItemKey);
        for (var i = 0; i < staffKeys.Count; i++)
        {
            Allocations.Add(new WorkItemAllocation
            {
                AllocationKey = Guid.NewGuid(),
                TenantId = tenantId,
                WorkItemKey = workItemKey,
                StaffKey = staffKeys[i],
                IsPrimary = i == 0,
                CreatedAtUtc = nowUtc
            });
        }

        return Task.CompletedTask;
    }

    public Task<Dependency> UpsertDependencyAsync(Dependency dependency, Guid tenantId)
    {
        dependency = dependency with { TenantId = tenantId };
        Dependencies.Add(dependency);
        return Task.FromResult(dependency);
    }

    public Task<Risk> UpsertRiskAsync(Risk risk, Guid tenantId)
    {
        risk = risk with { TenantId = tenantId };
        Risks.Add(risk);
        return Task.FromResult(risk);
    }

    public Task<Issue> UpsertIssueAsync(Issue issue, Guid tenantId)
    {
        issue = issue with { TenantId = tenantId };
        Issues.Add(issue);
        return Task.FromResult(issue);
    }

    public Task<TimeEntry> UpsertTimeEntryAsync(TimeEntry timeEntry, Guid tenantId)
    {
        var existing = TimeEntries.FirstOrDefault(t =>
            t.TenantId == tenantId && Matches(t.ExternalSource, t.ExternalId, timeEntry.ExternalSource, timeEntry.ExternalId));
        if (existing is not null)
        {
            TimeEntries.Remove(existing);
            timeEntry = timeEntry with { TimeEntryKey = existing.TimeEntryKey };
        }

        timeEntry = timeEntry with { TenantId = tenantId };
        TimeEntries.Add(timeEntry);
        return Task.FromResult(timeEntry);
    }

    public Task<Customer> UpsertCustomerAsync(Customer customer, Guid tenantId)
    {
        var existing = Customers.FirstOrDefault(c => c.CustomerKey == customer.CustomerKey && c.TenantId == tenantId);
        if (existing is not null)
        {
            Customers.Remove(existing);
        }

        customer = customer with { TenantId = tenantId };
        Customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<Programme> AssignProgrammeToCustomerAsync(Guid programmeKey, Guid? customerKey, Guid tenantId)
    {
        var existing = Programmes.First(p => p.ProgrammeKey == programmeKey && p.TenantId == tenantId);
        Programmes.Remove(existing);
        var updated = existing with { CustomerKey = customerKey };
        Programmes.Add(updated);
        return Task.FromResult(updated);
    }

    public Task<Programme> SetProgrammeBudgetAsync(Guid programmeKey, decimal? budgetAmount, string? budgetCurrency, Guid tenantId)
    {
        var existing = Programmes.First(p => p.ProgrammeKey == programmeKey && p.TenantId == tenantId);
        Programmes.Remove(existing);
        var updated = existing with { BudgetAmount = budgetAmount, BudgetCurrency = budgetCurrency };
        Programmes.Add(updated);
        return Task.FromResult(updated);
    }

    public Task<WorkstreamBaseline> LockBaselineAsync(Guid workstreamKey, decimal baselineHours, Guid? lockedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        var existing = WorkstreamBaselines.FirstOrDefault(b => b.WorkstreamKey == workstreamKey && b.TenantId == tenantId);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        var baseline = new WorkstreamBaseline
        {
            WorkstreamKey = workstreamKey,
            TenantId = tenantId,
            BaselineHours = baselineHours,
            LockedAtUtc = nowUtc,
            LockedByStaffKey = lockedByStaffKey
        };
        WorkstreamBaselines.Add(baseline);
        return Task.FromResult(baseline);
    }

    public Task<ChangeRequest> CreateChangeRequestAsync(ChangeRequest changeRequest, Guid tenantId)
    {
        changeRequest = changeRequest with { TenantId = tenantId };
        ChangeRequests.Add(changeRequest);
        return Task.FromResult(changeRequest);
    }

    public Task<ChangeRequest> DecideChangeRequestAsync(Guid changeRequestKey, ChangeRequestStatus status, Guid? decidedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        var existing = ChangeRequests.First(c => c.ChangeRequestKey == changeRequestKey && c.TenantId == tenantId);
        ChangeRequests.Remove(existing);
        var updated = existing with { Status = status, DecidedByStaffKey = decidedByStaffKey, DecidedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        ChangeRequests.Add(updated);
        return Task.FromResult(updated);
    }

    public Task<ProgrammeStakeholder> AddStakeholderAsync(ProgrammeStakeholder stakeholder, Guid tenantId)
    {
        stakeholder = stakeholder with { TenantId = tenantId };
        Stakeholders.Add(stakeholder);
        return Task.FromResult(stakeholder);
    }

    public Task RemoveStakeholderAsync(Guid programmeStakeholderKey, Guid tenantId)
    {
        Stakeholders.RemoveAll(s => s.ProgrammeStakeholderKey == programmeStakeholderKey && s.TenantId == tenantId);
        return Task.CompletedTask;
    }

    public Task<ReportingSnapshot> CaptureReportingSnapshotAsync(ReportingSnapshot snapshot, Guid tenantId)
    {
        snapshot = snapshot with { TenantId = tenantId };
        ReportingSnapshots.Add(snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<Alert> RaiseAlertAsync(Alert alert, Guid tenantId)
    {
        alert = alert with { TenantId = tenantId };
        Alerts.Add(alert);
        return Task.FromResult(alert);
    }

    public Task AcknowledgeAlertAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId)
    {
        var existing = Alerts.FirstOrDefault(a => a.AlertKey == alertKey && a.TenantId == tenantId);
        if (existing is not null)
        {
            Alerts.Remove(existing);
            Alerts.Add(existing with { AcknowledgedAtUtc = nowUtc, AcknowledgedByStaffKey = acknowledgedByStaffKey });
        }

        return Task.CompletedTask;
    }

    private static bool Matches(string? existingSource, string? existingId, string? source, string? id) =>
        source is not null && id is not null && existingSource == source && existingId == id;
}
