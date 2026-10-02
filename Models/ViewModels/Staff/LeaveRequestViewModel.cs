using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Models.ViewModels.Staff;

public sealed record LeaveRequestViewModel(
    Guid RequestId,
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    LeaveType Type,
    LeaveRequestStatus Status,
    string? Notes,
    DateTime CreatedAtUtc);
