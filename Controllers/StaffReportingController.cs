using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Reporting;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// PMO reporting hub (Gold layer): the hub page, its CSV export, the
/// Admin-only Cost Summary and its export, and the trend history. Team Lead
/// or above only, same gate as StaffProgrammeOverviewController. A Team
/// Lead's contributor-capacity rows are scoped to their own StaffProfile.Team
/// (self-lookup only; there's no arbitrary-member team lookup, see
/// docs/programme-ops.md). Cost is a separate Admin-only action/route, not a
/// flag on the main page, so the query is structurally unreachable without
/// the check — same principle as ProgrammeOverviewViewModel carrying no cost
/// field at all.
///
/// The rest of /staffops/reporting lives in sibling controllers that share
/// the route prefix: alerts (StaffReportingAlertsController), the RAID
/// register (StaffRaidController), governance (StaffGovernanceController)
/// and customers/budgets (StaffPortfolioAdminController). This was one
/// 831-line controller; the URLs are unchanged, which RouteContractTests
/// checks.
///
/// Tenant boundary: every action takes a [CurrentTenant] tenantId, refused
/// (403) after the capability check when unresolved, same as the rest of
/// Programme Ops (docs/tenancy.md).
/// </summary>
[Route("staffops/reporting")]
[RequireFeature(ProductFeature.ReportingHub)]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffReportingController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    IReportingQueryService reportingQueryService,
    IProgrammeBudgetService programmeBudgetService,
    IReportingSnapshotService reportingSnapshotService,
    IAlertDetectionService alertDetectionService,
    IPortfolioAdminService portfolioAdminService,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, Guid? programmeKey = null, Guid? customerKey = null, string? message = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryResolvePeriod(from, to, out var periodStart, out var periodEnd, out var periodError))
        {
            return RedirectToAction(nameof(Index), new { programmeKey, customerKey, message = periodError });
        }

        var (isAdmin, teamFilter) = await ResolveTeamFilterAsync();
        var hub = await reportingQueryService.BuildHubAsync(periodStart, periodEnd, teamFilter, tenantId, programmeKey, customerKey, cancellationToken);
        if (!hub.Scope.IsValid) return NotFound();

        // Available to every Team Lead+ caller — used for the filter
        // dropdowns, not just the Admin-only customer-management form below
        // them on the page (that section stays gated by CanViewCost).
        var (customers, programmes) = await portfolioAdminService.GetFilterOptionsAsync(tenantId);
        var openAlertCount = await alertDetectionService.CountOpenAlertsAsync(tenantId, teamFilter, cancellationToken);

        ViewData["Title"] = localizer["Reporting Hub"];
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Reporting/Index.cshtml",
            new ReportingHubPageViewModel(hub, isAdmin, customers, programmes, programmeKey, customerKey, openAlertCount));
    }

    [HttpGet("export")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Export([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, Guid? programmeKey = null, Guid? customerKey = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!TryResolvePeriod(from, to, out var periodStart, out var periodEnd, out var periodError))
        {
            return BadRequest(periodError);
        }

        var (_, teamFilter) = await ResolveTeamFilterAsync();
        var model = await reportingQueryService.BuildHubAsync(periodStart, periodEnd, teamFilter, tenantId, programmeKey, customerKey, cancellationToken);
        if (!model.Scope.IsValid) return NotFound();

        return File(ReportingCsv.Hub(model), "text/csv", $"reporting-hub-{periodStart:yyyy-MM-dd}-to-{periodEnd:yyyy-MM-dd}.csv");
    }

    [HttpGet("cost")]
    [RequireCapability(Capability.ViewCommercials)]
    public async Task<IActionResult> Cost([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        if (!TryResolvePeriod(from, to, out var periodStart, out var periodEnd, out var periodError))
        {
            return BadRequest(periodError);
        }

        var summary = await reportingQueryService.BuildCostSummaryAsync(periodStart, periodEnd, tenantId, cancellationToken);
        var budgets = await programmeBudgetService.BuildBudgetSummaryAsync(tenantId, cancellationToken);

        ViewData["Title"] = localizer["Cost Summary"];
        return View("~/Views/StaffOps/Reporting/Cost.cshtml", new CostPageViewModel(summary, budgets));
    }

    [HttpGet("cost/export")]
    [RequireCapability(Capability.ViewCommercials)]
    public async Task<IActionResult> ExportCost([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        if (!TryResolvePeriod(from, to, out var periodStart, out var periodEnd, out var periodError))
        {
            return BadRequest(periodError);
        }

        var model = await reportingQueryService.BuildCostSummaryAsync(periodStart, periodEnd, tenantId, cancellationToken);

        return File(ReportingCsv.Cost(model), "text/csv", $"cost-summary-{periodStart:yyyy-MM-dd}-to-{periodEnd:yyyy-MM-dd}.csv");
    }

    [HttpGet("trend")]
    [RequireCapability(Capability.ViewDeliveryReporting)]
    public async Task<IActionResult> Trend([CurrentTenant] Guid tenantId)
    {
        var history = await reportingSnapshotService.GetHistoryAsync(tenantId);

        ViewData["Title"] = localizer["Trend"];
        return View("~/Views/StaffOps/Reporting/Trend.cshtml", history);
    }

    [HttpPost("trend/capture")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.CaptureTrend)]
    public async Task<IActionResult> CaptureSnapshot([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null)
    {
        if (!TryResolvePeriod(from, to, out var periodStart, out var periodEnd, out var periodError))
        {
            return BadRequest(periodError);
        }

        var caller = await currentStaff.GetProfileAsync();
        var result = await reportingSnapshotService.CaptureAsync(periodStart, periodEnd, caller?.StaffKey, tenantId);
        if (result.Status != CommandStatus.Succeeded)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ViewData["Title"] = localizer["Trend"];
            ViewData["Message"] = result.Message;
            return View("~/Views/StaffOps/Reporting/Trend.cshtml", await reportingSnapshotService.GetHistoryAsync(tenantId));
        }

        return RedirectToAction(nameof(Trend));
    }

    /// <summary>
    /// An Admin (who can see cost) sees every team; anyone else sees only
    /// their own team's contributor rows.
    /// </summary>
    private async Task<(bool IsAdmin, string? TeamFilter)> ResolveTeamFilterAsync()
    {
        var isAdmin = await staffAuthorizationService.HasAsync(Capability.ViewCommercials);
        return isAdmin ? (true, null) : (false, ReportingTeamScope.For(false, (await currentStaff.GetProfileAsync())?.Team));
    }

    /// <summary>The last 30 days by default, both ends inclusive; bounded by <see cref="ReportingPeriod"/>.</summary>
    private bool TryResolvePeriod(DateOnly? from, DateOnly? to, out DateOnly periodStart, out DateOnly periodEnd, out string? error)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return ReportingPeriod.TryResolve(from, to, today, 29, today, out periodStart, out periodEnd, out error);
    }
}
