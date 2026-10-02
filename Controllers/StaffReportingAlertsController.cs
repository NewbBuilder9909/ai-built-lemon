using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// In-app threshold alerts under /staffops/reporting/alerts. Viewing them is
/// Team Lead+ (ViewDeliveryReporting). Acknowledging, or re-running
/// detection, is ManageProjectRisk.
///
/// Detection is no longer a side effect of opening the Reporting Hub. It
/// runs after each successful programme sync and on demand here (Refresh);
/// see AlertDetectionService.
/// </summary>
[Route("staffops/reporting")]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffReportingAlertsController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService authorization,
    IAlertDetectionService alertDetectionService,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("alerts")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Alerts([CurrentTenant] Guid tenantId, int page = 1, CancellationToken cancellationToken = default)
    {
        var isAdmin = await authorization.HasAsync(Capability.ViewCommercials);
        var team = ReportingTeamScope.For(isAdmin, isAdmin ? null : (await currentStaff.GetProfileAsync())?.Team);
        var alerts = await alertDetectionService.GetOpenAlertsPageAsync(tenantId, new PageRequest(page), team, cancellationToken);

        ViewData["Title"] = localizer["Alerts"];
        return View("~/Views/StaffOps/Reporting/Alerts.cshtml", alerts);
    }

    [HttpPost("alerts/acknowledge")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> AcknowledgeAlert([CurrentTenant] Guid tenantId, Guid alertKey)
    {
        var caller = await currentStaff.GetProfileAsync();
        await alertDetectionService.AcknowledgeAsync(alertKey, caller?.StaffKey, timeProvider.GetUtcNow().UtcDateTime, tenantId);

        return RedirectToAction(nameof(Alerts));
    }

    [HttpPost("alerts/refresh")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageProjectRisk)]
    public async Task<IActionResult> RefreshAlerts([CurrentTenant] Guid tenantId)
    {
        await alertDetectionService.DetectForTenantAsync(tenantId, timeProvider.GetUtcNow().UtcDateTime);

        return RedirectToAction(nameof(Alerts));
    }
}
