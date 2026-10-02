namespace ProgrammePulse.Models.Staff;

/// <summary>
/// A GDPR data-subject export — every field this application holds against
/// one StaffKey. Admin-triggered (a subject access request comes in via the
/// Admin, not self-service), so including StaffRate history here is
/// consistent with the rest of the codebase's cost-isolation rule: that
/// rule is about which code paths may ever fetch StaffRate, and this is an
/// Admin-gated path, same as StaffAdminController.Detail.
/// </summary>
public sealed record GdprExport(
    GdprExportProfile Profile,
    IReadOnlyList<GdprExportAvailabilityRow> Availability,
    IReadOnlyList<GdprExportLeaveRequestRow> LeaveRequests,
    IReadOnlyList<GdprExportRateRow> RateHistory,
    IReadOnlyList<GdprExportWorkHoursRow> WorkHoursHistory,
    IReadOnlyList<GdprExportAuditRow> AuditTrail,
    IReadOnlyList<GdprExportLinkedRecordRow> LinkedRecords,
    DateTime ExportedAtUtc);

/// <summary>
/// A record another feature area holds against the subject, contributed
/// through Services/Staff/IStaffDataParticipant (e.g. an identity link to a
/// ClickUp user id).
/// </summary>
public sealed record GdprExportLinkedRecordRow(
    string Section,
    string Summary,
    DateTime RecordedAtUtc);

/// <summary>An admin action recorded against the subject (creation, rate change, MFA reset, export, erasure…).</summary>
public sealed record GdprExportAuditRow(
    string EntityType,
    string Action,
    int? ActorMemberId,
    string? DetailJson,
    DateTime TimestampUtc);

public sealed record GdprExportProfile(
    Guid StaffKey,
    string FullName,
    string Email,
    string? JobTitle,
    string? Department,
    string? Team,
    bool IsActive,
    decimal DefaultWorkHoursPerWeek,
    DateTime CreatedAtUtc);

public sealed record GdprExportAvailabilityRow(
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    AvailabilityStatus Status,
    AvailabilitySource Source);

public sealed record GdprExportLeaveRequestRow(
    Guid RequestId,
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    LeaveType Type,
    LeaveRequestStatus Status,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc);

public sealed record GdprExportRateRow(
    decimal CostPerHour,
    string RateCurrency,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime ChangedAtUtc);

public sealed record GdprExportWorkHoursRow(
    decimal HoursPerWeek,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime ChangedAtUtc);
