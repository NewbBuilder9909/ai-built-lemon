using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Staff;

public interface ILeaveApprovalService
{
    Task<LeaveRequest> SubmitAsync(Guid staffKey, DateOnly requestedFrom, DateOnly requestedTo, LeaveType type, string? notes, Guid tenantId);

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> if the caller is not a
    /// Holiday Approver or Admin — checked here, not just at the controller, so
    /// the rule holds even if a future caller reaches this service another way.
    /// Throws <see cref="CrossTenantReferenceException"/> if
    /// requestId belongs to a different tenant or doesn't exist.
    /// </summary>
    Task<LeaveRequest> ApproveAsync(Guid requestId, Guid approvedByStaffKey, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">requestId belongs to a different tenant or doesn't exist.</exception>
    Task<LeaveRequest> RejectAsync(Guid requestId, Guid rejectedByStaffKey, string? reason, Guid tenantId);
}
