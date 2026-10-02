using System.Globalization;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class ProgrammeOverviewQueryService(
    IProgrammeReadRepository programmeRepository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : IProgrammeOverviewQueryService
{
    /// <summary>How far ahead the planned-allocation section looks.</summary>
    public const int PlannedWindowDays = 28;

    private static readonly WorkItemLifecycleStage[] ClosedStages =
    [
        WorkItemLifecycleStage.Done,
        WorkItemLifecycleStage.Cancelled
    ];

    public Task<ProgrammeOverviewViewModel> BuildOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        BuildOverviewAsync(tenantId, null, null, cancellationToken);

    public async Task<ProgrammeOverviewViewModel> BuildOverviewAsync(Guid tenantId, Guid? programmeKey, Guid? customerKey, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var programmes = await programmeRepository.GetProgrammesAsync(tenantId, cancellationToken);
        var customers = await programmeRepository.GetCustomersAsync(tenantId, cancellationToken);
        var scope = PortfolioScope.Resolve(programmes, customers, programmeKey, customerKey);
        var projects = await programmeRepository.GetProjectsAsync(tenantId, cancellationToken);
        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId, cancellationToken);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken);
        var allocations = await programmeRepository.GetAllocationsAsync(tenantId, cancellationToken);
        var plannedAllocations = await programmeRepository.GetPlannedAllocationsAsync(tenantId, cancellationToken);
        var staff = await staffRepository.GetByTenantAsync(tenantId, cancellationToken);

        var allWorkItems = workItems;
        programmes = programmes.Where(p => PortfolioScope.Includes(scope, p)).ToList();
        if (programmeKey is not null || customerKey is not null)
        {
            var programmeKeys = programmes.Select(p => p.ProgrammeKey).ToHashSet();
            projects = projects.Where(p => programmeKeys.Contains(p.ProgrammeKey)).ToList();
            var projectKeys = projects.Select(p => p.ProjectKey).ToHashSet();
            workstreams = workstreams.Where(w => projectKeys.Contains(w.ProjectKey)).ToList();
            var workstreamKeys = workstreams.Select(w => w.WorkstreamKey).ToHashSet();
            workItems = workItems.Where(w => workstreamKeys.Contains(w.WorkstreamKey)).ToList();
            plannedAllocations = plannedAllocations.Where(p => projectKeys.Contains(p.ProjectKey)).ToList();
        }

        var health = ProgrammeHealthCalculator.Build(programmes, customers, projects, workstreams, workItems, allWorkItems,
            await programmeRepository.GetRisksAsync(tenantId, cancellationToken),
            await programmeRepository.GetIssuesAsync(tenantId, cancellationToken),
            await programmeRepository.GetChangeRequestsAsync(tenantId, cancellationToken),
            await programmeRepository.GetDependenciesAsync(tenantId, cancellationToken),
            await programmeRepository.GetWorkstreamBaselinesAsync(tenantId, cancellationToken),
            await programmeRepository.GetStakeholdersAsync(tenantId, cancellationToken), staff, now);

        var programmesByKey = programmes.ToDictionary(p => p.ProgrammeKey);
        var projectsByKey = projects.ToDictionary(p => p.ProjectKey);
        var itemsByWorkstream = workItems.ToLookup(w => w.WorkstreamKey);
        var activeStaffByKey = staff.Where(s => s.IsActive).ToDictionary(s => s.StaffKey);

        var kpiCards = BuildKpiCards(workItems, now);
        var workstreamStatuses = BuildWorkstreamStatuses(workstreams, projectsByKey, programmesByKey, itemsByWorkstream, now);
        var milestones = BuildMilestones(workItems, workstreams, now);
        var resourceCapacity = BuildResourceCapacity(workItems, allocations, activeStaffByKey);
        var (planned, plannedCoverage) = BuildPlannedAllocations(plannedAllocations, activeStaffByKey, now);

        return new ProgrammeOverviewViewModel(kpiCards, workstreamStatuses, milestones, resourceCapacity, planned, plannedCoverage, now)
        {
            Scope = scope,
            ProgrammeHealth = health,
            OverdueByAge = BuildOverdueByAge(workItems, now),
            PastDueWithUnreadableStatus = workItems.Count(w => w.IsPastDueWithUnreadableStatus(now))
        };
    }

    /// <summary>
    /// Each headline figure carries its denominator: "3 blocked" means
    /// nothing until the reader knows it's 3 of 67 open items. The programme
    /// count was dropped; it answered no question anyone brings to the page.
    /// </summary>
    private static List<KpiCardViewModel> BuildKpiCards(IReadOnlyList<WorkItem> workItems, DateTime now)
    {
        var open = workItems.Where(w => !ClosedStages.Contains(w.Stage)).ToList();
        var blocked = open.Count(w => w.Stage == WorkItemLifecycleStage.Blocked);
        var overdue = open.Count(w => w.IsOverdueOn(now));
        var openMilestones = open.Count(w => w.IsMilestone);
        var overdueMilestones = open.Count(w => w.IsMilestone && w.IsOverdueOn(now));

        return
        [
            new KpiCardViewModel("Open Work Items", Count(open.Count), Numerator: open.Count, Denominator: workItems.Count, DenominatorKey: "Kpi.OfWorkItems"),
            new KpiCardViewModel("Blocked", Count(blocked), NeedsAttention: blocked > 0, Numerator: blocked, Denominator: open.Count, DenominatorKey: "Kpi.OfOpenItems"),
            new KpiCardViewModel("Overdue", Count(overdue), NeedsAttention: overdue > 0, Numerator: overdue, Denominator: open.Count, DenominatorKey: "Kpi.OfOpenItems"),
            new KpiCardViewModel("Overdue milestones", Count(overdueMilestones), NeedsAttention: overdueMilestones > 0,
                Numerator: overdueMilestones, Denominator: openMilestones, DenominatorKey: "Kpi.OfOpenMilestones")
        ];

        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Days late, and whether anyone is named against the item.</summary>
    private static List<OverdueAgeBandViewModel> BuildOverdueByAge(IReadOnlyList<WorkItem> workItems, DateTime now)
    {
        var overdue = workItems.Where(w => w.IsOverdueOn(now)).ToList();
        return OverdueAgeBandViewModel.Bands
            .Select(band =>
            {
                var inBand = overdue.Where(w => band.Contains(w.DaysPastDue(now))).ToList();
                return band with { WithOwner = inBand.Count(w => w.AssignedStaffKey is not null), WithoutOwner = inBand.Count(w => w.AssignedStaffKey is null) };
            })
            .ToList();
    }

    private static List<WorkstreamStatusViewModel> BuildWorkstreamStatuses(
        IReadOnlyList<Workstream> workstreams,
        IReadOnlyDictionary<Guid, Project> projectsByKey,
        IReadOnlyDictionary<Guid, Programme> programmesByKey,
        ILookup<Guid, WorkItem> itemsByWorkstream,
        DateTime now)
    {
        var rows = new List<WorkstreamStatusViewModel>();

        foreach (var workstream in workstreams)
        {
            var items = itemsByWorkstream[workstream.WorkstreamKey].ToList();
            var total = items.Count;
            var done = items.Count(i => i.Stage == WorkItemLifecycleStage.Done);
            var blocked = items.Count(i => i.Stage == WorkItemLifecycleStage.Blocked);
            var overdue = items.Count(i => i.IsOverdueOn(now));
            var percent = total == 0 ? 0 : (int)Math.Round(done * 100m / total);

            var projectName = "(unknown project)";
            var programmeName = "(unknown programme)";
            if (projectsByKey.TryGetValue(workstream.ProjectKey, out var project))
            {
                projectName = project.Name;
                if (programmesByKey.TryGetValue(project.ProgrammeKey, out var programme))
                {
                    programmeName = programme.Name;
                }
            }

            rows.Add(new WorkstreamStatusViewModel(programmeName, projectName, workstream.Name, total, done, blocked, overdue, percent));
        }

        return rows;
    }

    private static List<MilestoneStatusViewModel> BuildMilestones(
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyList<Workstream> workstreams,
        DateTime now)
    {
        var workstreamNames = workstreams.ToDictionary(w => w.WorkstreamKey, w => w.Name);

        return workItems
            .Where(w => w.IsMilestone)
            .OrderBy(w => w.DueDateUtc)
            .Select(w => new MilestoneStatusViewModel(
                w.Title,
                workstreamNames.GetValueOrDefault(w.WorkstreamKey, "(unknown workstream)"),
                w.DueDateUtc,
                w.Stage.ToDisplayLabel(),
                w.IsOverdueOn(now),
                w.IsPastDueWithUnreadableStatus(now),
                w.DaysPastDue(now),
                w.Stage.IsClosed()))
            .ToList();
    }

    /// <summary>
    /// Grouped by WorkItemAllocation, not WorkItem.AssignedStaffKey, but only
    /// the primary allocation on each work item counts toward a person's
    /// outstanding-hours total — ClickUp's "assignees" list doesn't
    /// distinguish who's actually doing the work from who's just tagged for
    /// visibility (there's no real RACI weighting here yet), so crediting
    /// every co-assignee the item's full EstimatedHours would inflate
    /// workload for everyone but the one person actually accountable for it.
    /// A work item with co-assignees still appears once per person in
    /// AssignedOpenItems for whoever is primary; secondary allocations are
    /// tracked (see WorkItemAllocation) but don't contribute hours here.
    /// Items with no estimate contribute nothing and are counted in
    /// OpenItemsWithoutEstimate so the reader can see the coverage gap.
    /// </summary>
    private static List<ResourceCapacityViewModel> BuildResourceCapacity(
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyList<WorkItemAllocation> allocations,
        IReadOnlyDictionary<Guid, Models.Staff.StaffProfile> activeStaffByKey)
    {
        var openWorkItemsByKey = workItems
            .Where(w => !ClosedStages.Contains(w.Stage))
            .ToDictionary(w => w.WorkItemKey);

        var openAllocationsByStaff = allocations
            .Where(a => a.IsPrimary && openWorkItemsByKey.ContainsKey(a.WorkItemKey) && activeStaffByKey.ContainsKey(a.StaffKey))
            .GroupBy(a => a.StaffKey);

        return openAllocationsByStaff
            .Select(g =>
            {
                var staff = activeStaffByKey[g.Key];
                var items = g.Select(a => openWorkItemsByKey[a.WorkItemKey]).ToList();
                return new ResourceCapacityViewModel(
                    staff.FullName,
                    staff.DefaultWorkHoursPerWeek,
                    items.Count,
                    items.Sum(i => i.EstimatedHours ?? 0m),
                    items.Count(i => i.EstimatedHours is null));
            })
            // By name, never by load: a list of people sorted by how much they
            // carry gets read from the bottom as a ranking (docs/delivery-load.md).
            .OrderBy(r => r.StaffFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Planned time inside [now, now + PlannedWindowDays]. Hours are summed
    /// only where the source recorded hours (AllocatedHours); an allocation
    /// with no hours is counted, not zeroed. Allocations whose person could
    /// not be resolved, and allocations with no dates, are reported in the
    /// coverage figures rather than dropped — the reader must be able to
    /// see how much of the picture is missing.
    /// </summary>
    private static (List<PlannedAllocationSummaryViewModel> Rows, PlannedAllocationCoverageViewModel Coverage) BuildPlannedAllocations(
        IReadOnlyList<PlannedAllocation> plannedAllocations,
        IReadOnlyDictionary<Guid, Models.Staff.StaffProfile> activeStaffByKey,
        DateTime now)
    {
        var windowEnd = now.AddDays(PlannedWindowDays);
        var undated = 0;
        var inWindow = new List<PlannedAllocation>();

        foreach (var allocation in plannedAllocations)
        {
            var start = allocation.StartUtc ?? allocation.EndUtc;
            var end = allocation.EndUtc ?? allocation.StartUtc;
            if (start is null || end is null)
            {
                undated++;
                continue;
            }

            if (start <= windowEnd && end >= now)
            {
                inWindow.Add(allocation);
            }
        }

        var rows = inWindow
            .Where(a => a.StaffKey is not null && activeStaffByKey.ContainsKey(a.StaffKey.Value))
            .GroupBy(a => a.StaffKey!.Value)
            .Select(g =>
            {
                var withHours = g.Where(a => a.AllocatedHours is not null).ToList();
                var staff = activeStaffByKey[g.Key];
                return new PlannedAllocationSummaryViewModel(
                    staff.FullName,
                    g.Count(),
                    withHours.Count == 0 ? null : withHours.Sum(a => a.AllocatedHours!.Value),
                    g.Count() - withHours.Count,
                    staff.DefaultWorkHoursPerWeek,
                    withHours.Count == 0 ? null : Math.Round(withHours.Sum(a => HoursInsideWindow(a, now, windowEnd)) / (PlannedWindowDays / 7m), 1));
            })
            // By name; an over-booked row is flagged in the view, not sorted to the top.
            .OrderBy(r => r.StaffFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var coverage = new PlannedAllocationCoverageViewModel(
            inWindow.Count,
            inWindow.Count(a => a.StaffKey is null || !activeStaffByKey.ContainsKey(a.StaffKey.Value)),
            inWindow.Count(a => a.AllocatedHours is null),
            undated,
            now,
            windowEnd);

        return (rows, coverage);
    }

    /// <summary>
    /// The share of a booking's recorded hours that falls inside the window,
    /// spread evenly over the booking's span. Only used for the weekly
    /// average; the table itself keeps showing hours as the source recorded
    /// them, so a booking straddling the window edge is not silently cut.
    /// </summary>
    private static decimal HoursInsideWindow(PlannedAllocation allocation, DateTime windowStart, DateTime windowEnd)
    {
        var start = (allocation.StartUtc ?? allocation.EndUtc)!.Value;
        var end = (allocation.EndUtc ?? allocation.StartUtc)!.Value;
        var span = (end - start).TotalHours;
        if (span <= 0)
            return allocation.AllocatedHours!.Value;

        var overlap = (Min(end, windowEnd) - Max(start, windowStart)).TotalHours;
        return allocation.AllocatedHours!.Value * (decimal)Math.Clamp(overlap / span, 0d, 1d);
    }

    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;

    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
}
