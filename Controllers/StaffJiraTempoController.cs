using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Jira issues set against Tempo worklogs, under /staffops/reporting/jira-tempo.
/// The page exists only for a tenant with Jira or Tempo data: without either
/// it returns 404, and the Reporting sub-nav doesn't show its link. Same
/// audience as the Reporting Hub (ViewDeliveryReporting); it names no one
/// and reads no cost.
/// </summary>
[Route("staffops/reporting")]
[RequireFeature(ProductFeature.ReportingHub)]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffJiraTempoController(
    IJiraTempoReconciliationQueryService reconciliation,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("jira-tempo")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, string? message = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (!ReportingPeriod.TryResolve(from, to, today, 29, today, out var start, out var end, out var periodError))
            return RedirectToAction(nameof(Index), new { message = periodError });

        var report = await reconciliation.BuildAsync(tenantId, start, end, cancellationToken);
        if (report is null) return NotFound();

        ViewData["Title"] = "Jira and Tempo";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Reporting/JiraTempo.cshtml", report);
    }
}
