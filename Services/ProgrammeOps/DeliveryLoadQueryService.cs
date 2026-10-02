using System.Globalization;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Gold. Projects Silver time entries and allocations into the weekly
/// concurrency series <see cref="DeliveryLoadFacts"/> works on, then hands
/// back findings about people whose own load has risen.
///
/// Source-agnostic by construction: it reads <see cref="TimeEntry"/> and
/// <see cref="WorkItemAllocation"/>, never a vendor client or a Bronze DTO,
/// so when Jira/Tempo land as a second source
/// (docs/jira-tempo-integration-scope.md) they write the same Silver rows
/// and this view works unchanged.
///
/// Two deliberate modelling choices:
///
/// <b>A context is a Project, not a work item.</b> Moving between items in
/// one project is cheap; moving between projects is the switch that costs.
/// Counting work items would make a focused person on a large project look
/// fragmented.
///
/// <b>Only time entries carry the weekly series.</b> They are the one Silver
/// row with a work date, so they are the only honest source of history.
/// Allocations have no date and describe the present, so they are reported
/// as current standing concurrency beside a finding and never trended —
/// projecting today's allocations backwards would fabricate a past.
///
/// No cost or rate data is read here, and none is reachable from this view
/// model; this follows the rule in CLAUDE.md that a view a non-Admin role
/// can reach must not retrieve StaffRate rows at all rather than hiding them.
/// </summary>
public sealed class DeliveryLoadQueryService(
    IProgrammeReadRepository programmeRepository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider,
    IDeliveryRoleDirectory? deliveryRoles = null) : IDeliveryLoadQueryService
{
    private static readonly WorkItemLifecycleStage[] ClosedStages =
    [
        WorkItemLifecycleStage.Done,
        WorkItemLifecycleStage.Cancelled
    ];

    private readonly DeliveryLoadThresholds _thresholds = new();

    public async Task<DeliveryLoadReport> BuildAsync(Guid tenantId, bool includePeople, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        // The last complete week, not the one in progress. Timesheets fill up
        // during a week, so judging the current one read every person as
        // "time logging stopped" each Monday morning: a false data exception
        // for the whole organisation, once a week.
        var currentWeekStart = StartOfIsoWeek(today).AddDays(-7);

        var staff = await staffRepository.GetByTenantAsync(tenantId, cancellationToken);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken);
        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId, cancellationToken);
        var weeklyCounts = await programmeRepository.GetWeeklyProjectCountsAsync(tenantId, cancellationToken);
        var datedSources = await programmeRepository.GetDatedTimeSourcesAsync(tenantId, cancellationToken);
        var allocations = await programmeRepository.GetAllocationsAsync(tenantId, cancellationToken);

        var projectByWorkstream = workstreams.ToDictionary(w => w.WorkstreamKey, w => w.ProjectKey);
        var projectByWorkItem = workItems
            .Where(i => projectByWorkstream.ContainsKey(i.WorkstreamKey))
            .ToDictionary(i => i.WorkItemKey, i => projectByWorkstream[i.WorkstreamKey]);

        var openItemKeys = workItems
            .Where(i => !ClosedStages.Contains(i.Stage))
            .Select(i => i.WorkItemKey)
            .ToHashSet();

        // Oversight-only members with no time and no open allocations carry
        // no delivery load; leaving them in reported them as a "gap in what
        // this page can see". Counted separately so the page can say so.
        var oversightOnly = deliveryRoles is null ? new HashSet<int>() : await deliveryRoles.GetOversightOnlyMemberIdsAsync();
        // One row per person-week of dated time, so its people are everyone who
        // has logged time, without reading the whole time history.
        var staffWithTime = weeklyCounts.Select(w => w.StaffKey).ToHashSet();
        var staffWithAllocations = allocations.Select(a => a.StaffKey).ToHashSet();
        bool IsOversightOnlyWithoutWork(Models.Staff.StaffProfile person) =>
            oversightOnly.Contains(person.MemberId)
            && !staffWithTime.Contains(person.StaffKey)
            && !staffWithAllocations.Contains(person.StaffKey);

        var activeStaff = staff.Where(s => s.IsActive && !IsOversightOnlyWithoutWork(s)).ToList();
        var oversightOnlyExcluded = staff.Count(s => s.IsActive && IsOversightOnlyWithoutWork(s));

        var openContextsByStaff = allocations
            .Where(a => openItemKeys.Contains(a.WorkItemKey) && projectByWorkItem.ContainsKey(a.WorkItemKey))
            .GroupBy(a => a.StaffKey)
            .ToDictionary(g => g.Key, g => g.Select(a => projectByWorkItem[a.WorkItemKey]).Distinct().Count());

        // An entry with no work date cannot be placed in a week. TimeEntry's
        // own contract says those stay out of period reporting rather than
        // being defaulted into the current one; the repository's weekly
        // series already excludes them, and is summed in the database.
        var weeksByStaff = weeklyCounts.ToLookup(w => w.StaffKey);

        var inputs = activeStaff
            .Select(person => new PersonLoadInput(
                person.StaffKey,
                BuildWeeks(weeksByStaff[person.StaffKey], currentWeekStart),
                openContextsByStaff.GetValueOrDefault(person.StaffKey, 0)))
            .ToList();

        var result = DeliveryLoadFacts.Calculate(inputs, currentWeekStart, _thresholds);

        var names = activeStaff.ToDictionary(p => p.StaffKey, p => p.FullName);

        var sources = datedSources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Ordered by name, not by how elevated anyone is. Sorting a findings
        // list by magnitude puts someone at the bottom of it, and a bottom is
        // the thing this whole design exists to not have.
        var rows = includePeople
            ? result.Findings
                .Select(f => new PersonLoadRow(
                    f.StaffKey,
                    names.GetValueOrDefault(f.StaffKey, "(unknown person)"),
                    f.Signal,
                    f.CurrentContexts,
                    f.BaselineContexts,
                    f.ConsecutiveWeeks,
                    f.OpenAllocatedContexts))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return new DeliveryLoadReport(
            rows,
            result.Findings.Count(f => f.Signal == LoadSignal.Elevated),
            result.Findings.Count(f => f.Signal == LoadSignal.Sustained),
            result.Findings.Count(f => f.Signal == LoadSignal.CoverageFellAway),
            result.PeopleInScope,
            result.PeopleWithSufficientHistory,
            result.PeopleWithoutTimeData,
            currentWeekStart,
            _thresholds,
            sources,
            includePeople,
            oversightOnlyExcluded);
    }

    private static List<WeeklyLoadObservation> BuildWeeks(
        IEnumerable<WeeklyProjectCount> weeks,
        DateOnly currentWeekStart)
    {
        return weeks
            .Where(w => w.WeekStart <= currentWeekStart)
            .Select(w => new WeeklyLoadObservation(
                w.WeekStart,
                w.DistinctProjects,
                // The week is in the series because something was logged in
                // it. Distinct contexts can still be zero when every entry is
                // against work this tenant has not synced.
                HasTimeData: true))
            .OrderByDescending(w => w.WeekStart)
            .ToList();
    }

    private static DateOnly StartOfIsoWeek(DateOnly date) => WeeklyProjectCount.WeekStartOf(date);
}

/// <summary>
/// The delivery load page's model. Carries its own coverage figures so a
/// reader can see how much of the team the findings are drawn from, and a
/// flag for whether names were permitted at all.
/// </summary>
public sealed record DeliveryLoadReport(
    IReadOnlyList<PersonLoadRow> Findings,
    int ElevatedCount,
    int SustainedCount,
    int CoverageFellAwayCount,
    int PeopleInScope,
    int PeopleWithSufficientHistory,
    int PeopleWithoutTimeData,
    DateOnly CurrentWeekStart,
    DeliveryLoadThresholds Thresholds,
    IReadOnlyList<string> Sources,
    bool IncludesPeople,
    int PeopleInOversightRolesOnly = 0)
{
    public string WeekLabel => CurrentWeekStart.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public int PeopleNotAssessed => PeopleInScope - PeopleWithSufficientHistory;
}

/// <summary>One named finding. Mirrors PersonLoadFinding and adds the name.</summary>
public sealed record PersonLoadRow(
    Guid StaffKey,
    string Name,
    LoadSignal Signal,
    int? CurrentContexts,
    int BaselineContexts,
    int ConsecutiveWeeks,
    int OpenAllocatedContexts);
