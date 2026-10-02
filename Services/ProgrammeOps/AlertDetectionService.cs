using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// The minimal notification channel the build plan called for before
/// threshold alerts could exist: in-app only (a persisted, dismissible
/// Alert row a Team Lead/Admin sees on the Reporting Hub), not email or a
/// chat webhook. This codebase has no outbound-email or webhook
/// infrastructure at all yet — building one untested, in a single pass,
/// alongside the alerts that would use it, is a materially bigger and
/// riskier piece of work than what "a minimal channel" implies. In-app is
/// real (persisted, dismissible, survives across visits) without that risk;
/// wiring a raised Alert to an actual email/webhook send is the natural
/// next step once real SMTP/webhook configuration exists.
///
/// Detection runs through DetectForTenantAsync, after every successful
/// programme sync (StaffProgrammeOverviewController.Sync) and on demand from
/// the Alerts page. It used to run on every Reporting Hub page *view*. That
/// was a GET that wrote rows and repeated the page's heaviest reads, and it
/// detected against whichever team the viewer could see, so a Team Lead's
/// visit only ever checked their own team. Scheduled detection belongs with
/// the background worker (architecture review, Phase 3).
/// </summary>
public sealed class AlertDetectionService(
    IProgrammeRepository programmeRepository,
    IStaffRepository staffRepository,
    IAvailabilityRepository availabilityRepository,
    IReportingQueryService reportingQueryService) : IAlertDetectionService
{
    /// <summary>The same default window the Reporting Hub opens on: the last 30 days.</summary>
    public const int DetectionWindowDays = 30;

    public async Task DetectForTenantAsync(Guid tenantId, DateTime nowUtc)
    {
        var periodEnd = DateOnly.FromDateTime(nowUtc);
        var hub = await reportingQueryService.BuildHubAsync(
            periodEnd.AddDays(-(DetectionWindowDays - 1)), periodEnd, teamFilter: null, tenantId);
        await DetectAndRaiseAsync(hub.ContributorCapacity, nowUtc, tenantId);
    }

    /// <summary>
    /// How far ahead UpcomingOverAllocation looks — a two-working-week
    /// resourcing horizon, matched to how far out due dates are usually
    /// trustworthy in this data. Not user-configurable; revisit only if a
    /// real need for a different horizon shows up.
    /// </summary>
    private const int ForwardWindowDays = 14;

    /// <summary>
    /// Below this and a contributor is flagged LowUtilisation for the
    /// current period, provided they had any available hours at all (see
    /// the guard where this is used). Not user-configurable; same
    /// not-a-real-need-yet reasoning as ForwardWindowDays.
    /// </summary>
    private const int LowUtilisationThresholdPercent = 50;

    private static readonly WorkItemLifecycleStage[] ClosedStages =
    [
        WorkItemLifecycleStage.Done,
        WorkItemLifecycleStage.Cancelled
    ];

    public async Task DetectAndRaiseAsync(IReadOnlyList<ContributorCapacityRowViewModel> contributorCapacity, DateTime nowUtc, Guid tenantId)
    {
        var openAlerts = await programmeRepository.GetOpenAlertsAsync(tenantId);
        var openEntityKeysByType = openAlerts
            .ToLookup(a => a.Type, a => a.EntityKey);

        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId);
        var itemsByWorkstream = workItems.ToLookup(w => w.WorkstreamKey);

        foreach (var workstream in workstreams)
        {
            var hasBlockedItem = itemsByWorkstream[workstream.WorkstreamKey].Any(i => i.Stage == WorkItemLifecycleStage.Blocked);
            if (!hasBlockedItem || openEntityKeysByType[AlertType.WorkstreamBlocked].Contains(workstream.WorkstreamKey))
            {
                continue;
            }

            await programmeRepository.RaiseAlertAsync(new Alert
            {
                AlertKey = Guid.NewGuid(),
                Type = AlertType.WorkstreamBlocked,
                EntityKey = workstream.WorkstreamKey,
                Message = $"Workstream \"{workstream.Name}\" has one or more blocked work items.",
                RaisedAtUtc = nowUtc
            }, tenantId);
        }

        foreach (var contributor in contributorCapacity)
        {
            if (contributor.ResidualCapacityHours >= 0m || openEntityKeysByType[AlertType.NegativeResidualCapacity].Contains(contributor.StaffKey))
            {
                continue;
            }

            await programmeRepository.RaiseAlertAsync(new Alert
            {
                AlertKey = Guid.NewGuid(),
                Type = AlertType.NegativeResidualCapacity,
                EntityKey = contributor.StaffKey,
                Message = $"{contributor.StaffFullName}'s residual capacity is {contributor.ResidualCapacityHours:0.##} hours for the current period — over-allocated.",
                RaisedAtUtc = nowUtc
            }, tenantId);
        }

        foreach (var contributor in contributorCapacity)
        {
            // Reconstituted rather than carried on the row: ResidualCapacityHours
            // is availableHours - loggedHours, so availableHours is the sum of
            // the two. Guards against flagging someone who was on full leave —
            // UtilisationPercent is 0 whenever availableHours is 0, which isn't
            // "under-utilised", it's "had nothing to log against".
            var availableHours = contributor.ResidualCapacityHours + contributor.LoggedHours;
            if (availableHours <= 0m
                || contributor.UtilisationPercent >= LowUtilisationThresholdPercent
                || openEntityKeysByType[AlertType.LowUtilisation].Contains(contributor.StaffKey))
            {
                continue;
            }

            await programmeRepository.RaiseAlertAsync(new Alert
            {
                AlertKey = Guid.NewGuid(),
                Type = AlertType.LowUtilisation,
                EntityKey = contributor.StaffKey,
                Message = $"{contributor.StaffFullName}'s utilisation is {contributor.UtilisationPercent}% for the current period — well under capacity.",
                RaisedAtUtc = nowUtc
            }, tenantId);
        }

        await DetectUpcomingOverAllocationsAsync(workItems, openEntityKeysByType[AlertType.UpcomingOverAllocation].ToHashSet(), nowUtc, tenantId);
    }

    /// <summary>
    /// Forward-looking counterpart to the NegativeResidualCapacity check
    /// above: that one only fires once a contributor has already logged more
    /// hours than they had available in a closed period. This asks the
    /// PMBOK "resource levelling" question instead — is this person already
    /// committed, on paper, past their calendar for the days immediately
    /// ahead — using WorkItem.EstimatedHours on their primary allocations
    /// (see WorkItemAllocation and ProgrammeOverviewQueryService.
    /// BuildResourceCapacity for why only the primary allocation counts)
    /// due within ForwardWindowDays, before any time has been logged at all.
    /// </summary>
    private async Task DetectUpcomingOverAllocationsAsync(IReadOnlyList<WorkItem> workItems, HashSet<Guid> alreadyAlertedStaffKeys, DateTime nowUtc, Guid tenantId)
    {
        var windowStart = DateOnly.FromDateTime(nowUtc);
        var windowEnd = windowStart.AddDays(ForwardWindowDays - 1);

        var workItemsByKey = workItems.ToDictionary(w => w.WorkItemKey);
        var allocations = await programmeRepository.GetAllocationsAsync(tenantId);

        var committedHoursByStaff = allocations
            .Where(a => a.IsPrimary && workItemsByKey.TryGetValue(a.WorkItemKey, out var item)
                && !ClosedStages.Contains(item.Stage)
                && item.DueDateUtc is not null
                && IsWithin(DateOnly.FromDateTime(item.DueDateUtc.Value), windowStart, windowEnd))
            .GroupBy(a => a.StaffKey)
            .ToDictionary(g => g.Key, g => g.Sum(a => workItemsByKey[a.WorkItemKey].EstimatedHours ?? 0m));

        if (committedHoursByStaff.Count == 0)
        {
            return;
        }

        var staff = await staffRepository.GetByTenantAsync(tenantId);
        var weekdays = CountWeekdays(windowStart, windowEnd);

        foreach (var person in staff.Where(s => s.IsActive))
        {
            if (!committedHoursByStaff.TryGetValue(person.StaffKey, out var committedHours) || alreadyAlertedStaffKeys.Contains(person.StaffKey))
            {
                continue;
            }

            var dailyHours = person.DefaultWorkHoursPerWeek / 5m;
            var baselineWindowHours = weekdays * dailyHours;

            var leaveRows = await availabilityRepository.GetForStaffAsync(person.StaffKey, windowStart, windowEnd);
            var leaveHours = leaveRows
                .Where(a => a.Status != AvailabilityStatus.Available)
                .Sum(a => LeaveHoursFor(a, dailyHours));

            var availableWindowHours = Math.Max(0m, baselineWindowHours - leaveHours);
            if (committedHours <= availableWindowHours)
            {
                continue;
            }

            await programmeRepository.RaiseAlertAsync(new Alert
            {
                AlertKey = Guid.NewGuid(),
                Type = AlertType.UpcomingOverAllocation,
                EntityKey = person.StaffKey,
                Message = $"{person.FullName} is committed to {committedHours:0.##} hours of estimated work due in the next {ForwardWindowDays} days but only has {availableWindowHours:0.##} hours of capacity in that window.",
                RaisedAtUtc = nowUtc
            }, tenantId);
        }
    }

    /// <summary>Same shape as ReportingQueryService's identically-named helper — duplicated rather than shared, see that class for why.</summary>
    private static decimal LeaveHoursFor(Availability availability, decimal dailyHours)
    {
        if (availability.StartTime == TimeOnly.MinValue && availability.EndTime == TimeOnly.MaxValue)
        {
            return dailyHours;
        }

        var hours = (decimal)(availability.EndTime - availability.StartTime).TotalHours;
        return Math.Min(Math.Max(hours, 0m), dailyHours);
    }

    private static int CountWeekdays(DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsWithin(DateOnly date, DateOnly start, DateOnly end) => date >= start && date <= end;

    public async Task<ResultPage<Alert>> GetOpenAlertsPageAsync(Guid tenantId, PageRequest page, string? teamFilter = null, CancellationToken cancellationToken = default) =>
        await programmeRepository.GetOpenAlertsPageAsync(tenantId, page, await VisibleStaffAsync(tenantId, teamFilter, cancellationToken), cancellationToken);

    public async Task<int> CountOpenAlertsAsync(Guid tenantId, string? teamFilter = null, CancellationToken cancellationToken = default) =>
        await programmeRepository.CountOpenAlertsAsync(tenantId, await VisibleStaffAsync(tenantId, teamFilter, cancellationToken), cancellationToken);

    private async Task<IReadOnlyCollection<Guid>?> VisibleStaffAsync(Guid tenantId, string? teamFilter, CancellationToken cancellationToken)
    {
        if (teamFilter is null) return null; // Explicit tenant-wide administrative scope.
        if (string.IsNullOrWhiteSpace(teamFilter)) return [];
        return (await staffRepository.GetByTenantAsync(tenantId, cancellationToken))
            .Where(s => s.IsActive && string.Equals(s.Team, teamFilter, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.StaffKey).ToArray();
    }

    public Task AcknowledgeAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId) =>
        programmeRepository.AcknowledgeAlertAsync(alertKey, acknowledgedByStaffKey, nowUtc, tenantId);
}
