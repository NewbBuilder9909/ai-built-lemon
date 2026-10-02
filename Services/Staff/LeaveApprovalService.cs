using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Submit/approve/reject workflow for leave requests. Approving auto-creates the
/// corresponding Availability row (Status=Holiday, Source=LeavePolicy) so later
/// phases' calendar/ClickUp availability overlay picks up approved leave without
/// needing to know about the leave workflow itself.
/// </summary>
public sealed class LeaveApprovalService(
    ILeaveRequestRepository leaveRequestRepository,
    IAvailabilityRepository availabilityRepository,
    IStaffAuthorizationService staffAuthorizationService,
    TimeProvider timeProvider) : ILeaveApprovalService
{
    public Task<LeaveRequest> SubmitAsync(Guid staffKey, DateOnly requestedFrom, DateOnly requestedTo, LeaveType type, string? notes, Guid tenantId)
    {
        if (requestedTo < requestedFrom)
        {
            throw new ArgumentException("Requested end date cannot be before the start date.", nameof(requestedTo));
        }

        var request = new LeaveRequest
        {
            RequestId = Guid.NewGuid(),
            StaffKey = staffKey,
            RequestedFrom = requestedFrom,
            RequestedTo = requestedTo,
            Type = type,
            Status = LeaveRequestStatus.Pending,
            Notes = notes,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        return leaveRequestRepository.CreateAsync(request, tenantId);
    }

    public async Task<LeaveRequest> ApproveAsync(Guid requestId, Guid approvedByStaffKey, Guid tenantId)
    {
        if (!await staffAuthorizationService.HasAsync(Capability.ApproveLeave))
        {
            throw new UnauthorizedAccessException("Only a Holiday Approver or Admin can approve leave requests.");
        }

        var request = await leaveRequestRepository.GetByRequestKeyAsync(requestId, tenantId)
            ?? throw new CrossTenantReferenceException("LeaveRequest", requestId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var approved = request with
        {
            Status = LeaveRequestStatus.Approved,
            ApprovedByStaffKey = approvedByStaffKey,
            DecidedAtUtc = now
        };

        await leaveRequestRepository.UpdateAsync(approved, tenantId);

        for (var date = approved.RequestedFrom; date <= approved.RequestedTo; date = date.AddDays(1))
        {
            await availabilityRepository.CreateAsync(new Availability
            {
                StaffKey = approved.StaffKey,
                Date = date,
                StartTime = TimeOnly.MinValue,
                EndTime = TimeOnly.MaxValue,
                Status = AvailabilityStatus.Holiday,
                Source = AvailabilitySource.LeavePolicy
            });
        }

        return approved;
    }

    public async Task<LeaveRequest> RejectAsync(Guid requestId, Guid rejectedByStaffKey, string? reason, Guid tenantId)
    {
        if (!await staffAuthorizationService.HasAsync(Capability.ApproveLeave))
        {
            throw new UnauthorizedAccessException("Only a Holiday Approver or Admin can reject leave requests.");
        }

        var request = await leaveRequestRepository.GetByRequestKeyAsync(requestId, tenantId)
            ?? throw new CrossTenantReferenceException("LeaveRequest", requestId);

        var rejected = request with
        {
            Status = LeaveRequestStatus.Rejected,
            ApprovedByStaffKey = rejectedByStaffKey,
            Notes = reason ?? request.Notes,
            DecidedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        await leaveRequestRepository.UpdateAsync(rejected, tenantId);

        return rejected;
    }
}
