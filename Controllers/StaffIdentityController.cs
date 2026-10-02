using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The identity queue: people the syncs saw in ClickUp / Hub Planner but
/// could not match to a StaffProfile, and the explicit links that resolve
/// them. An email match is only a suggestion here, approved in one click;
/// nothing is attributed by email alone. Admin-only — a link decides whose work and bookings a person's
/// capacity figures include, which is the same sensitivity as editing the
/// roster. Every link/unlink is audited to ProgrammeOps_AuditLog by
/// IIdentityQueueService. Tenant-scoped like the rest of Programme Ops
/// (docs/tenancy.md) — every action takes a [CurrentTenant] tenantId,
/// checked after the capability.
/// </summary>
[Route("staffops/programme/identities")]
public sealed class StaffIdentityController(
    ICurrentStaff currentStaff,
    IIdentityQueueService identityQueue) : Controller
{
    public const string AuditEntityType = IdentityQueueService.AuditEntityType;
    private const string BasePath = "/staffops/programme/identities";

    [HttpGet("")]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null, int page = 1)
    {
        var model = await identityQueue.BuildAsync(tenantId, message, new PageRequest(page));

        ViewData["Title"] = "People to match";
        return View("~/Views/StaffOps/Programme/Identities.cshtml", model);
    }

    [HttpPost("link")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Link([CurrentTenant] Guid tenantId, Guid unresolvedIdentityKey, Guid staffKey) =>
        RedirectWithMessage(await identityQueue.LinkAsync(tenantId, unresolvedIdentityKey, staffKey, await currentStaff.GetMemberIdAsync()));

    [HttpPost("approve")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Approve([CurrentTenant] Guid tenantId, Guid unresolvedIdentityKey) =>
        RedirectWithMessage(await identityQueue.ApproveSuggestionAsync(tenantId, unresolvedIdentityKey, await currentStaff.GetMemberIdAsync()));

    [HttpPost("unlink")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Unlink([CurrentTenant] Guid tenantId, Guid linkKey) =>
        RedirectWithMessage(await identityQueue.UnlinkAsync(tenantId, linkKey, await currentStaff.GetMemberIdAsync()));

    private IActionResult RedirectWithMessage(string message) =>
        Redirect($"{BasePath}?message={Uri.EscapeDataString(message)}");
}
