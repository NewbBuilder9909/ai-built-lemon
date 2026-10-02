using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

[Route("staffops/platform/tenants/{tenantKey:guid}/onboarding")]
public sealed class StaffTenantOnboardingController(
    ISyncStatusQueryService sync,
    ITenantAdministratorProvisioningService provisioning, ICurrentStaff currentStaff) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> Index(Guid tenantKey)
    {
        var model = await provisioning.GetWorkspaceAsync(tenantKey);
        if (model is null) return NotFound();
        var sources = await sync.GetPublicationStatesAsync(tenantKey);
        ViewData["Title"] = "Organisation onboarding";
        return View("~/Views/StaffOps/Platform/Onboarding.cshtml",
            model with { Sources = sources });
    }

    [HttpPost("administrator")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> ProvisionAdministrator(Guid tenantKey, TenantAdministratorInput input)
    {
        var actor = await currentStaff.GetMemberIdAsync();
        if (actor is null) return Forbid();
        if (!ModelState.IsValid)
        {
            TempData["OnboardingMessage"] = "Enter a name, valid email and password of 12–128 characters.";
        }
        else
        {
            var result = await provisioning.ProvisionAsync(tenantKey, input, actor.Value);
            TempData["OnboardingMessage"] = result.Succeeded
                ? "Administrator created in this organisation. Arrange secure credential handover and confirm their first sign-in and MFA enrollment."
                : string.Join(" ", result.Errors);
        }
        // Passwords and submitted form values never enter URLs or persisted form state.
        return RedirectToAction(nameof(Index), new { tenantKey });
    }
}
