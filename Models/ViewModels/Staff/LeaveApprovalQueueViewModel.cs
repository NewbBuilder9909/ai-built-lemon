using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Models.ViewModels.Staff;

public sealed record LeaveApprovalQueueViewModel(IReadOnlyList<LeaveApprovalRowViewModel> PendingRequests);

public sealed record LeaveApprovalRowViewModel(
    Guid RequestId,
    string StaffFullName,
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    LeaveType Type,
    string? Notes,
    DateTime CreatedAtUtc);
