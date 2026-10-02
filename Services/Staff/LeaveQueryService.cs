using ProgrammePulse.Models.ViewModels.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Read side of leave: the approver's pending queue and a person's own
/// requests. Moved out of StaffApprovalsController and StaffPortalController.
/// Commands (submit, approve, reject) stay on ILeaveApprovalService.
/// </summary>
public interface ILeaveQueryService
{
    Task<LeaveApprovalQueueViewModel> BuildApprovalQueueAsync(Guid tenantId);

    Task<IReadOnlyList<LeaveRequestViewModel>> GetOwnRequestsAsync(Guid staffKey, Guid tenantId);
}

public sealed class LeaveQueryService(
    ILeaveRequestRepository leaveRequestRepository,
    IStaffRepository staffRepository) : ILeaveQueryService
{
    public async Task<LeaveApprovalQueueViewModel> BuildApprovalQueueAsync(Guid tenantId)
    {
        var pending = await leaveRequestRepository.GetPendingAsync(tenantId);

        // One roster read for the tenant rather than a staff lookup per
        // request. That also means a name can only come from the approver's
        // own tenant.
        var names = (await staffRepository.GetByTenantAsync(tenantId)).ToDictionary(s => s.StaffKey, s => s.FullName);

        var rows = pending
            .Select(request => new LeaveApprovalRowViewModel(
                request.RequestId,
                names.GetValueOrDefault(request.StaffKey, "(unknown staff)"),
                request.RequestedFrom,
                request.RequestedTo,
                request.Type,
                request.Notes,
                request.CreatedAtUtc))
            .ToList();

        return new LeaveApprovalQueueViewModel(rows);
    }

    public async Task<IReadOnlyList<LeaveRequestViewModel>> GetOwnRequestsAsync(Guid staffKey, Guid tenantId) =>
        (await leaveRequestRepository.GetForStaffAsync(staffKey, tenantId))
            .Select(r => new LeaveRequestViewModel(r.RequestId, r.RequestedFrom, r.RequestedTo, r.Type, r.Status, r.Notes, r.CreatedAtUtc))
            .ToList();
}
