using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Own-profile / own-availability / submit-leave actions for any logged-in
/// staff member — no role restriction beyond "is a member of one of the four
/// StaffRole groups", enforced per-action so a future addition can't forget it.
/// MyWork additionally resolves the caller's tenant (Forbid if unresolved) —
/// the only action here that reads ProgrammeOps data.
/// </summary>
[Route("staffops")]
public sealed class StaffPortalController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    ILeaveQueryService leaveQueryService,
    ILeaveApprovalService leaveApprovalService,
    IMyWorkQueryService myWorkQueryService,
    ITenantContext tenantContext,
    IModuleGate moduleGate) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // The profile and leave form are self-service, a benched module; with it
        // off, the bare /staffops address goes wherever this member lands.
        if (!await moduleGate.IsOnAsync(ProductModules.SelfService))
        {
            return Redirect(StaffLandingPage.ResolverPath);
        }

        if (!await staffAuthorizationService.HasAsync(Capability.ViewOwnProfile))
        {
            return Redirect("/staffops/account/login");
        }

        var staff = await currentStaff.GetProfileAsync();
        if (staff is null)
        {
            return NotFound("No staff profile is linked to this account.");
        }

        var tenantId = await tenantContext.ResolveTenantIdAsync();
        if (tenantId is null)
        {
            return Forbid();
        }

        var model = await leaveQueryService.GetOwnRequestsAsync(staff.StaffKey, tenantId.Value);

        ViewData["Title"] = "My Staff Profile";
        return View("~/Views/StaffOps/Index.cshtml", model);
    }

    /// <summary>
    /// Post-sign-in landing. Sends each role to the page it gets value from —
    /// see <see cref="StaffLandingPage"/> for the order and why it runs here
    /// rather than inside the login POST.
    /// </summary>
    [HttpGet("home")]
    public async Task<IActionResult> Home() =>
        Redirect(await StaffLandingPage.ResolveAsync(
            staffAuthorizationService, selfServiceOn: await moduleGate.IsOnAsync(ProductModules.SelfService)));

    /// <summary>
    /// Where a signed-in member lands when their role has nothing switched on:
    /// a Staff member while self-service is benched. Says so, rather than
    /// sending them to a page that refuses them.
    /// </summary>
    [HttpGet("start")]
    public async Task<IActionResult> Start()
    {
        if (await currentStaff.GetMemberIdAsync() is null)
        {
            return Redirect(StaffLandingPage.Login);
        }

        ViewData["Title"] = "Nothing to show yet";
        return View("~/Views/StaffOps/Start.cshtml");
    }

    [HttpGet("my-work")]
    [RequireModule(ProductModules.SelfService)]
    public async Task<IActionResult> MyWork(CancellationToken cancellationToken = default)
    {
        if (!await staffAuthorizationService.HasAsync(Capability.ViewOwnWork))
        {
            return Redirect("/staffops/account/login");
        }

        var staff = await currentStaff.GetProfileAsync();
        if (staff is null)
        {
            return NotFound("No staff profile is linked to this account.");
        }

        var tenantId = await tenantContext.ResolveTenantIdAsync();
        if (tenantId is null)
        {
            return Forbid();
        }

        var model = await myWorkQueryService.BuildAsync(staff.StaffKey, tenantId.Value, cancellationToken);

        ViewData["Title"] = "My Work";
        return View("~/Views/StaffOps/MyWork.cshtml", model);
    }

    [HttpPost("leave/submit")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.SubmitOwnLeave)]
    [RequireModule(ProductModules.SelfService)]
    public async Task<IActionResult> SubmitLeave(DateOnly requestedFrom, DateOnly requestedTo, LeaveType type, string? notes)
    {
        var staff = await currentStaff.GetProfileAsync();
        if (staff is null)
        {
            return NotFound("No staff profile is linked to this account.");
        }

        var tenantId = await tenantContext.ResolveTenantIdAsync();
        if (tenantId is null)
        {
            return Forbid();
        }

        await leaveApprovalService.SubmitAsync(staff.StaffKey, requestedFrom, requestedTo, type, notes, tenantId.Value);

        return Redirect("/staffops");
    }
}
