using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Captures a period-close snapshot on demand rather than via a scheduled
/// background job — this codebase has no recurring-task infrastructure yet
/// (every existing sync/report is user- or request-triggered), and adding
/// one is a bigger, separately-verifiable piece of architecture than "give
/// trend reporting something to show." An Admin/Team Lead clicking "Capture
/// snapshot" is the honest minimum version of the audit's "scheduled job"
/// recommendation; wiring it to Umbraco's RecurringHostedServiceBase is a
/// natural next step once this shape is proven.
///
/// What a snapshot holds is fixed by when it is taken, not by the period it
/// is filed under: effort figures are all-time totals and open and blocked
/// counts are as at capture; only utilisation covers the period. So a period
/// can only be captured close to its end. Before this rule, a snapshot taken
/// in September for July recorded September's figures under July's name
/// (docs/design-and-pmo-data-review-2026-09-30.md, B4).
/// </summary>
public sealed class ReportingSnapshotService(
    IReportingQueryService reportingQueryService,
    IProgrammeRepository programmeRepository,
    TimeProvider timeProvider) : IReportingSnapshotService
{
    /// <summary>How long after a period ends it can still be captured.</summary>
    public const int CaptureWindowDays = 7;

    public async Task<CommandResult> CaptureAsync(DateOnly periodStart, DateOnly periodEnd, Guid? capturedByStaffKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (RefusalFor(periodEnd, DateOnly.FromDateTime(now)) is { } refusal)
            return CommandResult.Refused(refusal);

        var hub = await reportingQueryService.BuildHubAsync(periodStart, periodEnd, teamFilter: null, tenantId);

        var snapshot = new ReportingSnapshot
        {
            SnapshotKey = Guid.NewGuid(),
            CapturedAtUtc = now,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalEstimatedHours = hub.EffortVariance.Sum(r => r.EstimatedHours),
            TotalActualHours = hub.EffortVariance.Sum(r => r.ActualHours),
            TotalVarianceHours = hub.EffortVariance.Sum(r => r.VarianceHours),
            OpenWorkItems = hub.CustomerRollup.Sum(r => r.OpenWorkItems),
            BlockedWorkItems = hub.CustomerRollup.Sum(r => r.BlockedWorkItems),
            AverageUtilisationPercent = hub.ContributorCapacity.Count > 0
                ? Math.Round(hub.ContributorCapacity.Average(r => (decimal)r.UtilisationPercent), 1)
                : null,
            CapturedByStaffKey = capturedByStaffKey
        };

        await programmeRepository.CaptureReportingSnapshotAsync(snapshot, tenantId);
        return CommandResult.Succeeded();
    }

    public Task<IReadOnlyList<ReportingSnapshot>> GetHistoryAsync(Guid tenantId) => programmeRepository.GetReportingSnapshotsAsync(tenantId);

    /// <summary>Why a period can't be captured today, or null when it can.</summary>
    public static string? RefusalFor(DateOnly periodEnd, DateOnly today)
    {
        if (periodEnd > today)
            return $"That period hasn't ended yet. Capture it on or after {periodEnd:d MMM yyyy}.";
        if (today.DayNumber - periodEnd.DayNumber > CaptureWindowDays)
            return $"That period ended more than {CaptureWindowDays} days ago. A snapshot records today's figures, so filing them under an earlier period would misstate it.";
        return null;
    }

    /// <summary>
    /// Days between a snapshot's period end and its capture, when it was taken
    /// after the capture window (only possible for snapshots recorded before
    /// the window existed). Null when it was captured in time.
    /// </summary>
    public static int? CapturedLateByDays(ReportingSnapshot snapshot)
    {
        var days = DateOnly.FromDateTime(snapshot.CapturedAtUtc).DayNumber - snapshot.PeriodEnd.DayNumber;
        return days > CaptureWindowDays ? days : null;
    }
}
