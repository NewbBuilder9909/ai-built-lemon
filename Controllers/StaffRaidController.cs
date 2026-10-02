using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The RAID register at /staffops/reporting/raid. Reading is ViewProjectRisk
/// and logging or moving a risk or issue is ManageProjectRisk. The rules live
/// in IRaidService; this controller binds, calls and redirects.
/// </summary>
[Route("staffops/reporting")]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffRaidController(
    IRaidService raidService,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("raid")]
    [RequireCapability(Capability.ViewProjectRisk)]
    public async Task<IActionResult> Raid([CurrentTenant] Guid tenantId, string? message = null, Guid? programmeKey = null, Guid? customerKey = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var model = await raidService.BuildAsync(tenantId, programmeKey, customerKey);
        if (!model.Scope.IsValid) return NotFound();

        ViewData["Title"] = localizer["RAID register"];
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Reporting/Raid.cshtml", model);
    }

    [HttpPost("raid/risks")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> CreateRisk([CurrentTenant] Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await raidService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.ProjectOptions.Any(p => p.ProjectKey == projectKey)) return NotFound();
        return BackToRegister(await raidService.CreateRiskAsync(tenantId, projectKey, title, description, severity), programmeFilter, customerFilter);
    }

    [HttpPost("raid/risks/status")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> UpdateRiskStatus([CurrentTenant] Guid tenantId, Guid riskKey, RiskStatus status, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await raidService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.Risks.Any(r => r.RiskKey == riskKey)) return NotFound();
        await raidService.SetRiskStatusAsync(tenantId, riskKey, status);
        return BackToRegister(CommandOutcome.Ok, programmeFilter, customerFilter);
    }

    [HttpPost("raid/issues")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> CreateIssue([CurrentTenant] Guid tenantId, Guid projectKey, string? title, string? description, SeverityLevel severity, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await raidService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.ProjectOptions.Any(p => p.ProjectKey == projectKey)) return NotFound();
        return BackToRegister(await raidService.CreateIssueAsync(tenantId, projectKey, title, description, severity), programmeFilter, customerFilter);
    }

    [HttpPost("raid/issues/status")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> UpdateIssueStatus([CurrentTenant] Guid tenantId, Guid issueKey, IssueStatus status, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await raidService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.Issues.Any(i => i.IssueKey == issueKey)) return NotFound();
        await raidService.SetIssueStatusAsync(tenantId, issueKey, status);
        return BackToRegister(CommandOutcome.Ok, programmeFilter, customerFilter);
    }

    private IActionResult BackToRegister(CommandOutcome outcome, Guid? programmeFilter, Guid? customerFilter) =>
        RedirectToAction(nameof(Raid), new { message = outcome.Error, programmeKey = programmeFilter, customerKey = customerFilter });
}
