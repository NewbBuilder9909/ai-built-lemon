namespace ProgrammePulse.Models.Staff;

/// <summary>
/// A staff member's request for time off. RequestId is a Guid rather than the
/// repository's internal identity column so it's safe to expose in URLs
/// (approve/reject links) without leaking row-count information.
/// </summary>
public sealed record LeaveRequest
{
    public required Guid RequestId { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid StaffKey { get; init; }

    public required DateOnly RequestedFrom { get; init; }

    public required DateOnly RequestedTo { get; init; }

    public required LeaveType Type { get; init; }

    public required LeaveRequestStatus Status { get; init; }

    public Guid? ApprovedByStaffKey { get; init; }

    public string? Notes { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public DateTime? DecidedAtUtc { get; init; }
}
