namespace ProgrammePulse.Models.Staff;

/// <summary>
/// One block of a staff member's day marked as available/busy/etc. Later phases
/// populate these from calendar sync (Source = Calendar); Phase 1 only populates
/// them from approved leave (Source = LeavePolicy, Status = Holiday) — see
/// Services/Staff/LeaveApprovalService.
/// </summary>
public sealed record Availability
{
    public required Guid StaffKey { get; init; }

    public required DateOnly Date { get; init; }

    public required TimeOnly StartTime { get; init; }

    public required TimeOnly EndTime { get; init; }

    public required AvailabilityStatus Status { get; init; }

    public required AvailabilitySource Source { get; init; }
}
