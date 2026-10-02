namespace ProgrammePulse.Models.Programme;

/// <summary>
/// One immutable, manually attributed effort estimate captured before work starts.
/// Only an explicitly reviewed completed item may enter calibration history.
/// </summary>
public sealed record EstimateBaseline
{
    public required Guid EstimateBaselineKey { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid WorkItemKey { get; init; }
    public required Guid EstimatorStaffKey { get; init; }
    public required decimal OriginalEffortHours { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public Guid? ReviewedByStaffKey { get; init; }
    public bool IsComparable { get; init; }
    public string? ReviewNote { get; init; }
}
