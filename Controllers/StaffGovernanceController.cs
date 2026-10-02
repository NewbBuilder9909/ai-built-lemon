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
/// PMO governance at /staffops/reporting/governance: change requests,
/// stakeholders and dependencies. Also locking a workstream's estimate
/// baseline, which is started from the Reporting Hub. Each command has its
/// own capability. The rules live in IGovernanceService.
/// </summary>
[Route("staffops/reporting")]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffGovernanceController(
    ICurrentStaff currentStaff,
    IGovernanceService governanceService,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("governance")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Governance([CurrentTenant] Guid tenantId, string? message = null, Guid? programmeKey = null, Guid? customerKey = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var model = await governanceService.BuildAsync(tenantId, programmeKey, customerKey);
        if (!model.Scope.IsValid) return NotFound();

        ViewData["Title"] = localizer["PMO Governance"];
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Reporting/Governance.cshtml", model);
    }

    [HttpPost("governance/changes")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DecideChangeRequest)]
    public async Task<IActionResult> CreateChangeRequest([CurrentTenant] Guid tenantId, Guid projectKey, string? title, string? description, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await governanceService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.ProjectOptions.Any(p => p.ProjectKey == projectKey)) return NotFound();
        return BackToGovernance(await governanceService.ProposeChangeAsync(tenantId, projectKey, title, description, await CallerStaffKeyAsync()), programmeFilter, customerFilter);
    }

    [HttpPost("governance/changes/decide")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DecideChangeRequest)]
    public async Task<IActionResult> DecideChangeRequest([CurrentTenant] Guid tenantId, Guid changeRequestKey, ChangeRequestStatus status, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await governanceService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.ChangeRequests.Any(c => c.ChangeRequestKey == changeRequestKey)) return NotFound();
        await governanceService.DecideChangeAsync(tenantId, changeRequestKey, status, await CallerStaffKeyAsync());
        return BackToGovernance(CommandOutcome.Ok, programmeFilter, customerFilter);
    }

    [HttpPost("governance/stakeholders")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStakeholders)]
    public async Task<IActionResult> AddStakeholder([CurrentTenant] Guid tenantId, Guid programmeKey, Guid? staffKey, string? externalName, StakeholderRole role, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await governanceService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.ProgrammeOptions.Any(p => p.ProgrammeKey == programmeKey)) return NotFound();
        return BackToGovernance(await governanceService.AddStakeholderAsync(tenantId, programmeKey, staffKey, externalName, role), programmeFilter, customerFilter);
    }

    [HttpPost("governance/stakeholders/remove")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageStakeholders)]
    public async Task<IActionResult> RemoveStakeholder([CurrentTenant] Guid tenantId, Guid programmeStakeholderKey, Guid? programmeFilter = null, Guid? customerFilter = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var scope = await governanceService.BuildAsync(tenantId, programmeFilter, customerFilter);
        if (!scope.Scope.IsValid || !scope.Stakeholders.Any(s => s.ProgrammeStakeholderKey == programmeStakeholderKey)) return NotFound();
        await governanceService.RemoveStakeholderAsync(tenantId, programmeStakeholderKey);
        return BackToGovernance(CommandOutcome.Ok, programmeFilter, customerFilter);
    }

    [HttpPost("baseline/lock")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.LockBaseline)]
    public async Task<IActionResult> LockBaseline([CurrentTenant] Guid tenantId, Guid workstreamKey, decimal baselineHours, Guid? programmeFilter = null, Guid? customerFilter = null, DateOnly? from = null, DateOnly? to = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await governanceService.LockBaselineAsync(tenantId, workstreamKey, baselineHours, await CallerStaffKeyAsync(), programmeFilter, customerFilter)) return NotFound();

        // Started from the Reporting Hub's effort-variance table, so it returns there.
        return RedirectToAction(nameof(StaffReportingController.Index), "StaffReporting", new { programmeKey = programmeFilter, customerKey = customerFilter, from, to });
    }

    private async Task<Guid?> CallerStaffKeyAsync() => (await currentStaff.GetProfileAsync())?.StaffKey;

    private IActionResult BackToGovernance(CommandOutcome outcome, Guid? programmeFilter, Guid? customerFilter) =>
        RedirectToAction(nameof(Governance), new { message = outcome.Error, programmeKey = programmeFilter, customerKey = customerFilter });
}
