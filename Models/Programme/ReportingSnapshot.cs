namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A period-close snapshot of the Reporting Hub's portfolio-wide totals,
/// captured on demand (not by a scheduled job — see
/// IReportingSnapshotService's doc comment for why) so "how did we trend
/// over the quarter" has something to answer from. Carries no cost/rate
/// field, same principle as ReportingHubViewModel — this is built from a
/// team-unfiltered BuildHubAsync call, which itself never touches
/// IStaffRateRepository.
/// </summary>
public sealed record ReportingSnapshot
{
    public required Guid SnapshotKey { get; init; }

    public Guid? TenantId { get; init; }

    public required DateTime CapturedAtUtc { get; init; }

    public required DateOnly PeriodStart { get; init; }

    public required DateOnly PeriodEnd { get; init; }

    public required decimal TotalEstimatedHours { get; init; }

    public required decimal TotalActualHours { get; init; }

    public required decimal TotalVarianceHours { get; init; }

    public required int OpenWorkItems { get; init; }

    public required int BlockedWorkItems { get; init; }

    public decimal? AverageUtilisationPercent { get; init; }

    public Guid? CapturedByStaffKey { get; init; }
}
