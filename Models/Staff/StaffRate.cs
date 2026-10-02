namespace ProgrammePulse.Models.Staff;

/// <summary>
/// A time-bounded hourly cost rate for a staff member. Rows are append-only —
/// changing a rate inserts a new row with EffectiveFromUtc = now and closes the
/// previous row's EffectiveToUtc, rather than updating a row in place — so rate
/// history is naturally audited without a separate audit table. Admin-only data:
/// nothing outside Services/Staff/IStaffRateRepository and the admin controller
/// should ever query StaffOps_StaffRate.
/// </summary>
public sealed record StaffRate
{
    public required Guid StaffKey { get; init; }

    public required decimal CostPerHour { get; init; }

    public required string RateCurrency { get; init; }

    public required DateTime EffectiveFromUtc { get; init; }

    public DateTime? EffectiveToUtc { get; init; }

    public required Guid ChangedByStaffKey { get; init; }

    public required DateTime ChangedAtUtc { get; init; }
}
