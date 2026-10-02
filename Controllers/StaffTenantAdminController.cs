using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.Commercial;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Platform-operator console: tenant lifecycle (Trial → Active → Suspended →
/// Archived), plan assignment, and the support signals needed to look after
/// customers (staff counts per tenant, orphaned staff rows, last sync
/// outcomes). Gated on the Platform Admin group — *not* Admin — on every
/// action: a tenant's own Admin must never be able to change their plan or
/// reactivate a suspended account (see Models/Staff/StaffRole.PlatformAdmin).
///
/// Every change is written to StaffOps_AuditLog (entity type "Tenant") by
/// ITenantAdminService and is visible on /staffops/admin/audit. A tenant is
/// never deleted: it is Archived and its data ages out per
/// docs/data-governance.md. The one exception is a customer's delivery data
/// at the end of a diagnostic, which "Delete delivery data" removes on
/// request once the tenant is Suspended or Archived
/// (IDeliveryDataPurgeService). That audit row lives in StaffOps_AuditLog,
/// which the purge doesn't touch, so it survives as the record of the
/// deletion.
/// </summary>
[Route("staffops/platform/tenants")]
public sealed class StaffTenantAdminController(
    ICurrentStaff currentStaff,
    ITenantContext tenantContext,
    ITenantAdminService tenantAdmin,
    ITenantModuleService modules,
    ISyncStatusQueryService syncStatusQueryService,
    IDeliveryDataPurgeService deliveryDataPurge) : Controller
{
    private const string BasePath = "/staffops/platform/tenants";

    [HttpGet("")]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> Index(string? message = null)
    {
        await tenantContext.EnsureResolvedAsync();
        var (tenantRows, staffWithoutTenant) = await tenantAdmin.BuildTenantRowsAsync(tenantContext.CurrentTenantId);
        var rows = await modules.WithCommercialTermsAsync(tenantRows);
        var sources = await syncStatusQueryService.GetPublicationStatesAcrossTenantsAsync();

        var model = new TenantAdminViewModel(
            rows,
            TenantPlan.All,
            Enum.GetValues<TenantStatus>(),
            modules.SellableModules(),
            new PlatformSupportViewModel(sources, staffWithoutTenant),
            message);

        ViewData["Title"] = "Tenants";
        return View("~/Views/StaffOps/Platform/Tenants.cshtml", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> Create(string name, string shortCode, string plan, TenantStatus status = TenantStatus.Trial, int? trialDays = 30) =>
        RedirectWithMessage(await tenantAdmin.CreateAsync(name, shortCode, plan, status, trialDays, await currentStaff.GetMemberIdAsync()));

    [HttpPost("{tenantKey:guid}/status")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> SetStatus(Guid tenantKey, TenantStatus status, int? trialDays = null)
    {
        await tenantContext.EnsureResolvedAsync();
        var result = await tenantAdmin.SetStatusAsync(tenantKey, status, trialDays, tenantContext.CurrentTenantId, await currentStaff.GetMemberIdAsync());
        return result.Found ? RedirectWithMessage(result.Message) : NotFound();
    }

    [HttpPost("{tenantKey:guid}/plan")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> SetPlan(Guid tenantKey, string plan)
    {
        var result = await tenantAdmin.SetPlanAsync(tenantKey, plan, await currentStaff.GetMemberIdAsync());
        if (!result.Found)
        {
            return NotFound();
        }

        // A plan change may leave modules the new plan can't hold.
        if (TenantPlan.IsKnown(plan))
        {
            await modules.NormaliseForPlanAsync(tenantKey, TenantPlan.Normalize(plan));
        }

        return RedirectWithMessage(result.Message);
    }

    [HttpPost("{tenantKey:guid}/modules")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> SetModules(Guid tenantKey, string[]? selectedModules)
    {
        var message = await modules.SetModulesAsync(tenantKey, selectedModules, await currentStaff.GetMemberIdAsync());
        return message is null ? NotFound() : RedirectWithMessage(message);
    }

    private const string PurgeView = "~/Views/StaffOps/Platform/PurgeDeliveryData.cshtml";

    /// <summary>What deleting this tenant's delivery data would remove, table by table.</summary>
    [HttpGet("{tenantKey:guid}/purge")]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> PurgePreview(Guid tenantKey)
    {
        var preview = await deliveryDataPurge.PreviewAsync(tenantKey);
        if (preview is null)
            return NotFound();

        ViewData["Title"] = $"Delete delivery data: {preview.Tenant.Name}";
        return View(PurgeView, new PurgeDeliveryDataViewModel(preview, null));
    }

    [HttpPost("{tenantKey:guid}/purge")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManagePlatform)]
    public async Task<IActionResult> Purge(Guid tenantKey, string? confirmShortCode)
    {
        var result = await deliveryDataPurge.PurgeAsync(tenantKey, confirmShortCode, await currentStaff.GetMemberIdAsync());
        var preview = await deliveryDataPurge.PreviewAsync(tenantKey);
        if (preview is null)
            return NotFound();

        if (!result.Purged)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }

        ViewData["Title"] = $"Delete delivery data: {preview.Tenant.Name}";
        return View(PurgeView, new PurgeDeliveryDataViewModel(preview, result));
    }

    private IActionResult RedirectWithMessage(string message) =>
        Redirect($"{BasePath}?message={Uri.EscapeDataString(message)}");
}
