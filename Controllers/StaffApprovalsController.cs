using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Leave approval queue — Holiday Approver or Admin only (ApproveLeave on
/// every action; ControllerGateTests fails the build if a new action has no
/// declared gate).
///
/// Tenant boundary: every action takes a [CurrentTenant] tenantId, refused
/// (403) after the capability check when unresolved, same as the rest of
/// Staff Ops (docs/tenancy.md) — without this, GetPendingAsync would leak
/// every tenant's pending leave requests into one Holiday Approver's queue.
/// </summary>
[Route("staffops/approvals")]
[RequireModule(ProductModules.SelfService)]
public sealed class StaffApprovalsController(
    ICurrentStaff currentStaff,
    ILeaveQueryService leaveQueryService,
    ILeaveApprovalService leaveApprovalService) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ApproveLeave)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId)
    {
        var model = await leaveQueryService.BuildApprovalQueueAsync(tenantId);

        ViewData["Title"] = "Leave Approvals";
        return View("~/Views/StaffOps/Approvals/Index.cshtml", model);
    }

    [HttpPost("{requestId:guid}/approve")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ApproveLeave)]
    public async Task<IActionResult> Approve([CurrentTenant] Guid tenantId, Guid requestId)
    {
        var approver = (await currentStaff.GetProfileAsync())?.StaffKey;
        if (approver is null)
        {
            return NotFound("No staff profile is linked to this account.");
        }

        await leaveApprovalService.ApproveAsync(requestId, approver.Value, tenantId);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{requestId:guid}/reject")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ApproveLeave)]
    public async Task<IActionResult> Reject([CurrentTenant] Guid tenantId, Guid requestId, string? reason)
    {
        var approver = (await currentStaff.GetProfileAsync())?.StaffKey;
        if (approver is null)
        {
            return NotFound("No staff profile is linked to this account.");
        }

        await leaveApprovalService.RejectAsync(requestId, approver.Value, reason, tenantId);

        return RedirectToAction(nameof(Index));
    }
}
