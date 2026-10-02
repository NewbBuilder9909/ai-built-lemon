using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class ReportingQueryService(
    IProgrammeReadRepository programmeRepository,
    IStaffRepository staffRepository,
    IAvailabilityRepository availabilityRepository,
    IStaffRateRepository staffRateRepository,
    IWorkHoursHistoryRepository workHoursHistoryRepository,
    IProgrammeOverviewQueryService overviewQueryService,
    TimeProvider timeProvider,
    IDeliveryRoleDirectory? deliveryRoles = null) : IReportingQueryService
{
    private static readonly WorkItemLifecycleStage[] ClosedStages =
    [
        WorkItemLifecycleStage.Done,
        WorkItemLifecycleStage.Cancelled
    ];

    /// <summary>
    /// teamFilter only scopes contributor capacity (staff-centric data — a
    /// StaffProfile has a Team, a workstream doesn't). programmeFilter/
    /// customerFilter scope delivery KPIs, effort variance and customer rollup.
    /// Capacity and unattributed-time coverage remain explicitly tenant/team-wide:
    /// filtering their hours without a corresponding capacity allocation would misstate utilisation.
    /// </summary>
    public async Task<ReportingHubViewModel> BuildHubAsync(
        DateOnly periodStart, DateOnly periodEnd, string? teamFilter, Guid tenantId,
        Guid? programmeFilter = null, Guid? customerFilter = null, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var programmes = await programmeRepository.GetProgrammesAsync(tenantId, cancellationToken);
        var projects = await programmeRepository.GetProjectsAsync(tenantId, cancellationToken);
        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId, cancellationToken);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken);
        // Bounded by what the page shows (finding A1): the period's entries,
        // one summed figure per work item for cumulative effort variance, and
        // the two tenant-wide facts the page footnotes. Never the whole history.
        var periodEntries = await programmeRepository.GetTimeEntriesAsync(tenantId, periodStart, periodEnd, cancellationToken);
        var loggedByWorkItem = await programmeRepository.GetLoggedHoursByWorkItemAsync(tenantId, cancellationToken: cancellationToken);
        var coverage = await programmeRepository.GetTimeEntryCoverageAsync(tenantId, cancellationToken);
        var customers = await programmeRepository.GetCustomersAsync(tenantId, cancellationToken);
        var staff = await staffRepository.GetByTenantAsync(tenantId, cancellationToken);
        var baselines = await programmeRepository.GetWorkstreamBaselinesAsync(tenantId, cancellationToken);

        var overview = await overviewQueryService.BuildOverviewAsync(tenantId, programmeFilter, customerFilter, cancellationToken);

        var programmesByKey = programmes.ToDictionary(p => p.ProgrammeKey);
        var projectsByKey = projects.ToDictionary(p => p.ProjectKey);
        var itemsByWorkstream = workItems.ToLookup(w => w.WorkstreamKey);
        var baselineByWorkstream = baselines.ToDictionary(b => b.WorkstreamKey);

        var scopedWorkstreams = FilterWorkstreamsByProgrammeOrCustomer(workstreams, projectsByKey, programmesByKey, programmeFilter, customerFilter);

        var effortVariance = BuildEffortVariance(scopedWorkstreams, projectsByKey, programmesByKey, itemsByWorkstream, loggedByWorkItem, baselineByWorkstream);
        var scopedProgrammes = programmes.Where(p => PortfolioScope.Includes(overview.Scope, p)).ToList();
        var customerRollup = BuildCustomerRollup(scopedProgrammes, customers, projectsByKey, scopedWorkstreams, itemsByWorkstream);
        var (contributorCapacity, teamCapacitySubtotals, excludedOversightStaff) = await BuildContributorCapacityAsync(staff, periodEntries, periodStart, periodEnd, teamFilter, cancellationToken);
        var unlinkedTime = periodEntries
            .Where(entry => entry.WorkItemKey is null)
            .Select(entry => new UnlinkedTimeEntryViewModel(
                entry.ExternalSource ?? "Unknown", entry.ExternalId ?? entry.TimeEntryKey.ToString("D"),
                entry.ReportDate!.Value, entry.DurationHours, entry.StaffKey is not null))
            .OrderByDescending(entry => entry.WorkDate)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ToList();
        var unknownBillabilityHours = periodEntries
            .Where(entry => !entry.BillabilityKnown)
            .Sum(entry => entry.DurationHours);

        return new ReportingHubViewModel(
            periodStart,
            periodEnd,
            teamFilter,
            overview.KpiCards,
            effortVariance,
            contributorCapacity,
            teamCapacitySubtotals,
            customerRollup,
            unlinkedTime,
            unknownBillabilityHours,
            now,
            coverage.HasTempoEntries,
            coverage.UndatedHours,
            excludedOversightStaff) { Scope = overview.Scope };
    }

    private static List<Workstream> FilterWorkstreamsByProgrammeOrCustomer(
        IReadOnlyList<Workstream> workstreams,
        IReadOnlyDictionary<Guid, Project> projectsByKey,
        IReadOnlyDictionary<Guid, Programme> programmesByKey,
        Guid? programmeFilter,
        Guid? customerFilter)
    {
        if (programmeFilter is null && customerFilter is null)
        {
            return workstreams.ToList();
        }

        return workstreams.Where(w =>
        {
            if (!projectsByKey.TryGetValue(w.ProjectKey, out var project) || !programmesByKey.TryGetValue(project.ProgrammeKey, out var programme))
            {
                return false;
            }

            if (programmeFilter is not null && programme.ProgrammeKey != programmeFilter)
            {
                return false;
            }

            return customerFilter is null || programme.CustomerKey == customerFilter;
        }).ToList();
    }

    /// <summary>
    /// Joins logged hours to the StaffRate effective on the entry's date
    /// (history, not just the current rate, so a past rate change doesn't
    /// retroactively re-cost old entries). Only called from the admin-gated
    /// "cost" controller action — never from BuildHubAsync.
    /// </summary>
    public async Task<CostSummaryViewModel> BuildCostSummaryAsync(DateOnly periodStart, DateOnly periodEnd, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var periodEntries = await programmeRepository.GetTimeEntriesAsync(tenantId, periodStart, periodEnd, cancellationToken);
        var coverage = await programmeRepository.GetTimeEntryCoverageAsync(tenantId, cancellationToken);
        var staff = await staffRepository.GetByTenantAsync(tenantId, cancellationToken);
        var staffByKey = staff.ToDictionary(s => s.StaffKey);

        var inPeriod = periodEntries.Where(t => t.StaffKey is not null).ToList();

        var unpricedHours = periodEntries
            .Where(t => t.StaffKey is null)
            .Sum(t => t.DurationHours);

        var rateHistory = await staffRateRepository.GetHistoryAsync(
            inPeriod.Select(t => t.StaffKey!.Value).Distinct().ToList(), cancellationToken);

        var rows = new List<CostSummaryRowViewModel>();
        foreach (var group in inPeriod.GroupBy(t => t.StaffKey!.Value))
        {
            var result = TimeEntryCostCalculator.Calculate(group.ToList(), new Dictionary<Guid, IReadOnlyList<StaffRate>> { [group.Key] = rateHistory[group.Key] });

            unpricedHours += result.UnpricedHours;

            if (result.PricedHours == 0m)
            {
                continue;
            }

            rows.Add(new CostSummaryRowViewModel(
                staffByKey.TryGetValue(group.Key, out var s) ? s.FullName : "(unknown staff)",
                result.PricedHours,
                result.BillableHours,
                result.Cost,
                result.Currency ?? string.Empty));
        }

        rows = rows.OrderByDescending(r => r.TotalCost).ToList();

        var totalsByCurrency = rows
            .GroupBy(r => r.Currency)
            .Select(g => new CostSummaryCurrencyTotalViewModel(g.Key, g.Sum(r => r.TotalCost)))
            .OrderByDescending(t => t.TotalCost)
            .ToList();

        var unknownBillabilityHours = periodEntries
            .Where(entry => !entry.BillabilityKnown)
            .Sum(entry => entry.DurationHours);
        return new CostSummaryViewModel(periodStart, periodEnd, rows, rows.Sum(r => r.TotalCost),
            unpricedHours, totalsByCurrency, unknownBillabilityHours,
            coverage.HasTempoEntries,
            coverage.UndatedHours);
    }

    private static List<EffortVarianceRowViewModel> BuildEffortVariance(
        IReadOnlyList<Workstream> workstreams,
        IReadOnlyDictionary<Guid, Project> projectsByKey,
        IReadOnlyDictionary<Guid, Programme> programmesByKey,
        ILookup<Guid, WorkItem> itemsByWorkstream,
        IReadOnlyDictionary<Guid, decimal> loggedByWorkItem,
        IReadOnlyDictionary<Guid, WorkstreamBaseline> baselineByWorkstream)
    {
        var rows = new List<EffortVarianceRowViewModel>();

        foreach (var workstream in workstreams)
        {
            var items = itemsByWorkstream[workstream.WorkstreamKey].ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var estimated = items.Sum(i => i.EstimatedHours ?? 0m);
            var actual = items.Sum(i => loggedByWorkItem.GetValueOrDefault(i.WorkItemKey));

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

            decimal? baselineHours = null;
            decimal? baselineVariance = null;
            decimal? forecastAtCompletion = null;
            decimal? forecastVariance = null;
            if (baselineByWorkstream.TryGetValue(workstream.WorkstreamKey, out var baseline))
            {
                baselineHours = baseline.BaselineHours;
                baselineVariance = actual - baseline.BaselineHours;

                // A task count (including cancellations) cannot establish earned value.
                // Leave forecasts unknown until an approved remaining-effort estimate exists.
            }

            rows.Add(new EffortVarianceRowViewModel(
                workstream.WorkstreamKey, programmeName, projectName, workstream.Name,
                estimated, actual, actual - estimated, baselineHours, baselineVariance,
                forecastAtCompletion, forecastVariance,
                items.Count, items.Count(i => i.EstimatedHours is null)));
        }

        return rows;
    }

    private static List<CustomerRollupRowViewModel> BuildCustomerRollup(
        IReadOnlyList<Programme> programmes,
        IReadOnlyList<Customer> customers,
        IReadOnlyDictionary<Guid, Project> projectsByKey,
        IReadOnlyList<Workstream> workstreams,
        ILookup<Guid, WorkItem> itemsByWorkstream)
    {
        var customersByKey = customers.ToDictionary(c => c.CustomerKey);
        var workstreamsByProject = workstreams.ToLookup(w => w.ProjectKey);

        var countsByProgramme = new Dictionary<Guid, (int Open, int Blocked)>();
        foreach (var project in projectsByKey.Values)
        {
            foreach (var workstream in workstreamsByProject[project.ProjectKey])
            {
                foreach (var item in itemsByWorkstream[workstream.WorkstreamKey])
                {
                    var (open, blocked) = countsByProgramme.GetValueOrDefault(project.ProgrammeKey);
                    if (!ClosedStages.Contains(item.Stage))
                    {
                        open++;
                    }
                    if (item.Stage == WorkItemLifecycleStage.Blocked)
                    {
                        blocked++;
                    }
                    countsByProgramme[project.ProgrammeKey] = (open, blocked);
                }
            }
        }

        return programmes
            .GroupBy(p => p.CustomerKey.HasValue && customersByKey.TryGetValue(p.CustomerKey.Value, out var customer)
                ? customer.Name
                : "(no customer)")
            .Select(g => new CustomerRollupRowViewModel(
                g.Key,
                g.Count(),
                g.Sum(p => countsByProgramme.GetValueOrDefault(p.ProgrammeKey).Open),
                g.Sum(p => countsByProgramme.GetValueOrDefault(p.ProgrammeKey).Blocked)))
            .OrderByDescending(r => r.ProgrammeCount)
            .ToList();
    }

    private async Task<(List<ContributorCapacityRowViewModel> Rows, List<TeamCapacitySubtotalRowViewModel> TeamSubtotals, List<string> ExcludedOversightStaff)> BuildContributorCapacityAsync(
        IReadOnlyList<StaffProfile> staff,
        IReadOnlyList<TimeEntry> periodEntries,
        DateOnly periodStart,
        DateOnly periodEnd,
        string? teamFilter,
        CancellationToken cancellationToken)
    {
        var loggedByStaff = periodEntries
            .Where(t => t.StaffKey is not null)
            .ToLookup(t => t.StaffKey!.Value);

        // Null is tenant-wide; any other value is one team, and a blank team is
        // nobody (ReportingTeamScope). A blank must never widen to everyone:
        // that is how a reader with no team used to see every named contributor.
        var scoped = staff.Where(s => s.IsActive);
        if (teamFilter is not null)
        {
            var team = teamFilter.Trim();
            scoped = scoped.Where(s => team.Length > 0 && string.Equals(s.Team?.Trim(), team, StringComparison.OrdinalIgnoreCase));
        }

        var people = scoped.ToList();
        var staffKeys = people.Select(s => s.StaffKey).ToList();
        var hoursHistoryByStaff = await workHoursHistoryRepository.GetHistoryAsync(staffKeys, cancellationToken);
        var leaveByStaff = await availabilityRepository.GetForStaffAsync(staffKeys, periodStart, periodEnd, cancellationToken);

        // Oversight-only members (Board, Analyst, Platform Admin) with no time
        // in the period carry no delivery capacity; counting them at 0% made
        // the team look under-used. Named on the page, never dropped silently,
        // and any recorded time brings a person back in — see
        // StaffRole.OversightOnly.
        var oversightOnly = deliveryRoles is null ? new HashSet<int>() : await deliveryRoles.GetOversightOnlyMemberIdsAsync();
        var excludedOversightStaff = new List<string>();

        var rows = new List<ContributorCapacityRowViewModel>();
        var rawByTeam = new List<(string? Team, decimal Baseline, decimal Leave, decimal Logged, decimal BillableLogged, decimal Available)>();
        foreach (var person in people)
        {
            if (oversightOnly.Contains(person.MemberId) && !loggedByStaff[person.StaffKey].Any())
            {
                excludedOversightStaff.Add(person.FullName);
                continue;
            }

            // Day-by-day against WorkHoursHistory, not a single period-wide
            // person.DefaultWorkHoursPerWeek — see WorkHoursHistory's doc
            // comment. A person who went from 37.5 to 22.5 hrs/wk mid-period
            // now gets each weekday's baseline (and each leave block's cap)
            // computed from whatever was actually effective that day, not
            // today's value applied retroactively to the whole period.
            var hoursHistory = hoursHistoryByStaff[person.StaffKey];
            decimal DailyHoursOn(DateOnly date) => EffectiveHoursPerWeek(hoursHistory, date, person.DefaultWorkHoursPerWeek) / 5m;

            decimal baselineHours = 0m;
            for (var date = periodStart; date <= periodEnd; date = date.AddDays(1))
            {
                if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    baselineHours += DailyHoursOn(date);
                }
            }

            var leaveRows = leaveByStaff[person.StaffKey];
            var leaveHours = leaveRows
                .Where(a => a.Status != AvailabilityStatus.Available)
                .Sum(a => LeaveHoursFor(a, DailyHoursOn(a.Date)));

            var personEntries = loggedByStaff[person.StaffKey];
            var loggedHours = personEntries.Sum(t => t.DurationHours);
            var billableLoggedHours = personEntries.Where(t => t.BillabilityKnown && t.IsBillable).Sum(t => t.DurationHours);
            var capacity = CapacityFacts.Calculate(baselineHours, leaveHours, null, null, loggedHours);
            var availableHours = capacity.AvailableHours;
            var utilisation = availableHours > 0m ? (int)Math.Round(loggedHours / availableHours * 100m) : 0;
            var billableUtilisation = availableHours > 0m ? (int)Math.Round(billableLoggedHours / availableHours * 100m) : 0;

            rows.Add(new ContributorCapacityRowViewModel(
                person.StaffKey,
                person.FullName,
                person.Team,
                baselineHours,
                leaveHours,
                loggedHours,
                capacity.ActualResidualHours,
                utilisation,
                billableUtilisation));

            rawByTeam.Add((person.Team, baselineHours, leaveHours, loggedHours, billableLoggedHours, availableHours));
        }

        var teamSubtotals = rawByTeam
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Team) ? "(No team)" : r.Team)
            .Select(g =>
            {
                var available = g.Sum(r => r.Available);
                var logged = g.Sum(r => r.Logged);
                var billableLogged = g.Sum(r => r.BillableLogged);
                return new TeamCapacitySubtotalRowViewModel(
                    g.Key!,
                    g.Count(),
                    g.Sum(r => r.Baseline),
                    g.Sum(r => r.Leave),
                    logged,
                    available - logged,
                    available > 0m ? (int)Math.Round(logged / available * 100m) : 0,
                    available > 0m ? (int)Math.Round(billableLogged / available * 100m) : 0);
            })
            .OrderBy(t => t.TeamName)
            .ToList();

        // By name, not by residual: sorted by load, the bottom of the list reads as
        // "who is doing least" (docs/delivery-load.md). Negative residuals are flagged in the view.
        return (rows.OrderBy(r => r.StaffFullName, StringComparer.OrdinalIgnoreCase).ToList(), teamSubtotals, excludedOversightStaff.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// A full-day leave marker (LeaveApprovalService writes StartTime=MinValue,
    /// EndTime=MaxValue for each approved leave day) counts as one lost working
    /// day, not a literal ~24-hour span. Partial-day rows use their actual span,
    /// clamped to a working day.
    /// </summary>
    private static decimal LeaveHoursFor(Availability availability, decimal dailyHours)
    {
        if (availability.StartTime == TimeOnly.MinValue && availability.EndTime == TimeOnly.MaxValue)
        {
            return dailyHours;
        }

        var hours = (decimal)(availability.EndTime - availability.StartTime).TotalHours;
        return Math.Min(Math.Max(hours, 0m), dailyHours);
    }

    /// <summary>
    /// The history row effective on the given date (EffectiveFromUtc &lt;=
    /// date &lt; EffectiveToUtc, or still-open), or fallback if no row covers
    /// it — the pre-migration/no-history-yet gap AddWorkHoursHistoryTable's
    /// doc comment describes, not an error case.
    /// </summary>
    private static decimal EffectiveHoursPerWeek(IReadOnlyList<WorkHoursHistory> history, DateOnly date, decimal fallback)
    {
        var asOf = date.ToDateTime(TimeOnly.MinValue);
        var match = history.FirstOrDefault(h => h.EffectiveFromUtc <= asOf && (h.EffectiveToUtc is null || h.EffectiveToUtc > asOf));
        return match?.HoursPerWeek ?? fallback;
    }

}

/// <summary>
/// Whose named capacity rows a reporting reader may see. Null means the whole
/// tenant and is only for a reader who also holds ViewCommercials (an Admin).
/// Anyone else is scoped to their own team, and a reader with no team gets an
/// empty string, which matches nobody: Board and Analyst keep the portfolio
/// view without named rows, the same rule the alert list applies.
/// </summary>
public static class ReportingTeamScope
{
    public static string? For(bool tenantWide, string? team) => tenantWide ? null : team?.Trim() ?? string.Empty;
}
