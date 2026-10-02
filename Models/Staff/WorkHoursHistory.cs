namespace ProgrammePulse.Models.Staff;

/// <summary>
/// A time-bounded weekly-hours figure for a staff member — the exact same
/// append-only shape as StaffRate (changing hours inserts a new row and
/// closes the previous row's EffectiveToUtc, rather than updating in
/// place), for the same reason: StaffProfile.DefaultWorkHoursPerWeek is a
/// single mutable "current" value, so a part-time transition mid-programme
/// silently rewrote every past period's baseline capacity before this
/// existed. Unlike StaffRate, DefaultWorkHoursPerWeek stays on StaffProfile
/// too — it's not cost/rate-sensitive data needing the same structural
/// isolation, and forward-looking views (workload backlog, current
/// capacity) legitimately want "the current value" without a history join.
/// This table is what ReportingQueryService.BuildContributorCapacityAsync
/// reads instead, specifically because it reports on a past period where
/// "current" can be wrong. StaffAdminController.SetWorkHours keeps both in
/// sync on every change.
/// </summary>
public sealed record WorkHoursHistory
{
    public required Guid StaffKey { get; init; }

    public required decimal HoursPerWeek { get; init; }

    public required DateTime EffectiveFromUtc { get; init; }

    public DateTime? EffectiveToUtc { get; init; }

    public required Guid ChangedByStaffKey { get; init; }

    public required DateTime ChangedAtUtc { get; init; }
}
