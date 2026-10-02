using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Settings → Modules: the toolbox where an Admin switches benched modules
/// on or off for their organisation. Every module starts off.
/// </summary>
[Route("staffops/settings/modules")]
[RequireCapability(Capability.ManageModules)]
public sealed class StaffModulesController(
    IModuleToolboxService toolbox,
    ICurrentStaff currentStaff,
    IOptions<ExecutiveReviewOptions> executiveReview) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null, string? error = null)
    {
        var rows = await toolbox.ListAsync(tenantId, AvailableOnDeployment);

        ViewData["Title"] = "Modules";
        return View("~/Views/StaffOps/Settings/Modules.cshtml", new ModulesPageViewModel(rows, message, error));
    }

    [HttpPost("{moduleKey}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set([CurrentTenant] Guid tenantId, string moduleKey, bool on)
    {
        var result = await toolbox.SetAsync(tenantId, moduleKey, on, await currentStaff.GetMemberIdAsync(), AvailableOnDeployment);

        var query = result.Status == CommandStatus.Succeeded ? "message=" : "error=";
        return Redirect("/staffops/settings/modules?" + query + Uri.EscapeDataString(result.Message ?? string.Empty));
    }

    // Executive review also needs the deployment's own setting.
    private bool AvailableOnDeployment(string moduleKey) =>
        moduleKey != ProductModules.ExecutiveReview || executiveReview.Value.Enabled;
}
