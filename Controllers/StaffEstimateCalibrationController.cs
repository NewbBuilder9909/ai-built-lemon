using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Controllers;

/// <summary>Board sees tenant aggregates; Admin alone can see and review individual estimator history.</summary>
[Route("staffops/reporting/estimate-calibration")]
[RequireFeature(ProductFeature.ReportingHub)]
[RequireModule(ProductModules.EstimateCalibration)]
public sealed class StaffEstimateCalibrationController(
    IStaffAuthorizationService authorization,
    EstimateCalibrationService calibration,
    ICurrentStaff currentStaff) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewEstimateCalibration)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId)
    {
        var isAdmin = await authorization.HasAsync(Capability.ViewEstimatorHistory);
        var model = await calibration.BuildReportAsync(tenantId, isAdmin);
        ViewData["Title"] = "Estimate calibration";
        return View("~/Views/StaffOps/Reporting/EstimateCalibration.cshtml", model);
    }

    [HttpPost("capture")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageEstimateCalibration)]
    public async Task<IActionResult> Capture([CurrentTenant] Guid tenantId, Guid workItemKey, Guid estimatorStaffKey)
    {
        try
        {
            await calibration.CaptureAsync(tenantId, workItemKey, estimatorStaffKey, await currentStaff.GetMemberIdAsync());
        }
        // Kept although CrossTenantReferenceExceptionFilter handles it
        // globally: it derives from InvalidOperationException, so without
        // this clause the one below would turn a 404 into a 400.
        catch (CrossTenantReferenceException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("review")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageEstimateCalibration)]
    public async Task<IActionResult> Review([CurrentTenant] Guid tenantId, Guid estimateBaselineKey, bool isComparable, string? note)
    {
        // The reviewer is recorded on the baseline, so they must be a person
        // in this tenant, not merely a signed-in member.
        var reviewer = await currentStaff.GetProfileAsync();
        if (reviewer?.TenantId != tenantId) return Forbid();
        try
        {
            var result = await calibration.ReviewAsync(tenantId, estimateBaselineKey, reviewer.StaffKey, isComparable, note, reviewer.MemberId);
            if (result is null) return NotFound();
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        return RedirectToAction(nameof(Index));
    }
}
