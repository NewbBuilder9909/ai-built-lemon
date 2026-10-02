using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.BrandingOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Reporting;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Programme Overview (Gold layer). Team Lead or above only — general Staff
/// see their own tasks/profile elsewhere (StaffPortalController), not the
/// cross-programme view. Sync triggers are Admin-only: the "how do I run a
/// local sync" mechanism described in docs/programme-ops.md, gated the same
/// way StaffAdminController gates cost-rate changes. The page also carries
/// the management header: per source publication state (last published /
/// running / failed) and the unresolved-identity count, so a reader knows
/// how current and how complete the numbers are.
///
/// There is one sync action for every source, resolved through
/// ISyncSourceRegistry (via IProgrammeSyncService), so adding a source needs
/// no change here. It previously had one hard-coded action per vendor, which
/// is the coupling Stage 0 removed.
/// </summary>
[Route("staffops/programme")]
public sealed class StaffProgrammeOverviewController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    IProgrammeOverviewQueryService overviewQueryService,
    ISyncStatusQueryService syncStatusQueryService,
    IProgrammeSyncService programmeSync,
    ITenantContext tenantContext,
    IBrandingThemeResolverService themeResolver,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewPortfolio)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? syncMessage = null, string? syncError = null, Guid? programmeKey = null, Guid? customerKey = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var overview = await overviewQueryService.BuildOverviewAsync(tenantId, programmeKey, customerKey, cancellationToken);
        if (!overview.Scope.IsValid) return NotFound();
        var sources = await syncStatusQueryService.GetPublicationStatesAsync(tenantId);
        var unresolvedCount = await programmeSync.CountUnresolvedIdentitiesAsync(tenantId);
        var isAdmin = await staffAuthorizationService.HasAsync(Capability.ViewCommercials);

        var model = new ProgrammeOverviewPageViewModel(
            overview,
            sources,
            unresolvedCount,
            isAdmin,
            isAdmin ? await programmeSync.GetEntitledSourcesAsync() : [],
            syncMessage,
            syncError);

        ViewData["Title"] = "Programmes";
        return View("~/Views/StaffOps/Programme/Index.cshtml", model);
    }

    /// <summary>
    /// The page's figures as a sectioned CSV, provenance first, for sharing
    /// with someone who works in a spreadsheet. Same capability and tenant
    /// as the page, and no cost or rate data, like the page.
    /// </summary>
    [HttpGet("export")]
    [RequireCapability(Capability.ViewPortfolio)]
    public async Task<IActionResult> Export([CurrentTenant] Guid tenantId, Guid? programmeKey = null, Guid? customerKey = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var overview = await overviewQueryService.BuildOverviewAsync(tenantId, programmeKey, customerKey, cancellationToken);
        if (!overview.Scope.IsValid) return NotFound();
        var sources = await syncStatusQueryService.GetPublicationStatesAsync(tenantId);
        var unresolvedCount = await programmeSync.CountUnresolvedIdentitiesAsync(tenantId);
        var page = new ProgrammeOverviewPageViewModel(overview, sources, unresolvedCount, false, [], null, null);
        var organisation = (await themeResolver.GetActiveThemeAsync(tenantId)).CompanyName;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var csv = ProgrammeOverviewCsv.Build(page, organisation, now);
        return File(CsvWriter.ToBytes(csv), "text/csv", $"programme-overview-{now:yyyy-MM-dd}.csv");
    }

    /// <summary>
    /// One action for every source. The plan entitlement is evaluated at
    /// runtime against the source's own FeatureKey rather than by a
    /// compile-time [RequireFeature] attribute, because the key is only
    /// known once the source is resolved. The check is otherwise identical
    /// and still fails closed: an unresolved tenant and a plan without the
    /// feature both refuse, and both are covered by
    /// StaffProgrammeOverviewControllerTests.
    /// </summary>
    [HttpPost("sync/{source}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sync")]
    [RequireCapability(Capability.TriggerSync)]
    public async Task<IActionResult> Sync(string source)
    {
        // Resolved before the tenant check so an unknown source is a 404
        // whatever the caller's tenant is — it never reveals whether some
        // other tenant has a source by that name.
        var syncSource = programmeSync.Find(source);
        if (syncSource is null)
        {
            return NotFound();
        }

        var tenantId = await tenantContext.ResolveTenantIdAsync();
        if (tenantId is null)
        {
            return Forbid();
        }

        if (!await programmeSync.IsEntitledAsync(syncSource))
        {
            return Forbid();
        }

        var result = await programmeSync.RunAsync(syncSource, tenantId.Value, await currentStaff.GetMemberIdAsync());
        return RedirectToAction(nameof(Index), result.Succeeded
            ? new { syncMessage = result.Message }
            : new { syncError = result.Message });
    }
}
