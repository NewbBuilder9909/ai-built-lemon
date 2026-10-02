using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.BrandingOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Admin-only branding configuration: draft/publish/rollback lifecycle, logo
/// and favicon upload, and version history. Every admin action declares
/// [RequireCapability(ManageBranding)], matching StaffAdminController, and
/// takes a [CurrentTenant] tenantId (403 when unresolved, see
/// Services/Tenancy/CurrentTenantAttribute), since every admin action here
/// requires a real tenant to scope its data to. The lifecycle rules, audit
/// and cache invalidation live in IBrandingAdminService.
///
/// The plan gate sits on each admin action rather than the class, because
/// the read-only theme.css action is intentionally ungated: serving
/// already-published colour/font values is not a privileged operation. It
/// still resolves tenant (for a signed-in member) but never forbids on an
/// unresolved one — pre-login/anonymous requests fall back to the platform
/// default theme instead, since branding-by-tenant only makes sense once
/// the viewer is known.
/// </summary>
[Route("staffops/branding")]
public sealed class StaffBrandingController(
    ICurrentStaff currentStaff,
    ITenantContext tenantContext,
    IBrandingAdminService brandingAdmin,
    IBrandingThemeResolverService brandingThemeResolverService) : Controller
{
    /// <summary>The largest branding asset (4 MB hero) plus room for the form fields.</summary>
    private const long MaxBrandingUploadBytes = 4 * 1024 * 1024 + 64 * 1024;

    [HttpGet("")]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message)
    {
        var model = await brandingAdmin.BuildAdminAsync(tenantId, message);

        ViewData["Title"] = "Branding";
        return View("~/Views/StaffOps/Branding/Index.cshtml", model);
    }

    [HttpPost("save-draft")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> SaveDraft(
        [CurrentTenant] Guid tenantId,
        string companyName,
        string? tenantUiLabel,
        string primaryColour,
        string secondaryColour,
        string accentColour,
        string textColour,
        string surfaceColour,
        string backgroundColour,
        int borderRadiusPx,
        string fontOption,
        string headerStyle,
        string footerStyle,
        bool darkModeEnabled,
        bool isActive)
    {
        var input = new BrandingDraftInput(companyName, tenantUiLabel, primaryColour, secondaryColour, accentColour,
            textColour, surfaceColour, backgroundColour, borderRadiusPx, fontOption, headerStyle, footerStyle,
            darkModeEnabled, isActive);

        var invalid = await brandingAdmin.SaveDraftAsync(tenantId, input, await currentStaff.GetMemberIdAsync());
        if (invalid is not null)
        {
            ViewData["Title"] = "Branding";
            return View("~/Views/StaffOps/Branding/Index.cshtml", invalid);
        }

        return BackToBranding("Draft saved.");
    }

    [HttpPost("publish")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> Publish([CurrentTenant] Guid tenantId)
    {
        var result = await brandingAdmin.PublishAsync(tenantId, await currentStaff.GetMemberIdAsync());
        return BackToBranding(result.Message);
    }

    [HttpGet("history")]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> History([CurrentTenant] Guid tenantId)
    {
        var model = await brandingAdmin.BuildHistoryAsync(tenantId);

        ViewData["Title"] = "Branding History";
        return View("~/Views/StaffOps/Branding/History.cshtml", model);
    }

    [HttpPost("rollback/{versionKey:guid}")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> Rollback([CurrentTenant] Guid tenantId, Guid versionKey)
    {
        var result = await brandingAdmin.RollbackAsync(tenantId, versionKey, await currentStaff.GetMemberIdAsync());

        // A failed rollback returns to the history the person chose it from.
        return result.Succeeded
            ? BackToBranding(result.Message)
            : Redirect("/staffops/branding/history?message=" + Uri.EscapeDataString(result.Message));
    }

    // Refused before the body is buffered; the service still checks per asset (2 MB logo).
    [HttpPost("upload-logo")]
    [RequestSizeLimit(MaxBrandingUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBrandingUploadBytes)]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> UploadLogo([CurrentTenant] Guid tenantId, IFormFile file) =>
        BackToBranding(await brandingAdmin.UploadAssetAsync(tenantId, file, BrandingAssetType.Logo, await currentStaff.GetMemberIdAsync()));

    [HttpPost("upload-favicon")]
    [RequestSizeLimit(MaxBrandingUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBrandingUploadBytes)]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> UploadFavicon([CurrentTenant] Guid tenantId, IFormFile file) =>
        BackToBranding(await brandingAdmin.UploadAssetAsync(tenantId, file, BrandingAssetType.Favicon, await currentStaff.GetMemberIdAsync()));

    [HttpGet("preview")]
    [RequireCapability(Capability.ManageBranding)]
    [RequireFeature(ProductFeature.Branding)]
    public async Task<IActionResult> Preview([CurrentTenant] Guid tenantId)
    {
        var theme = await brandingAdmin.BuildPreviewAsync(tenantId);
        if (theme is null)
        {
            return Redirect("/staffops/branding");
        }

        ViewData["Title"] = "Branding Preview";
        return View("~/Views/StaffOps/Branding/Preview.cshtml", theme);
    }

    [HttpGet("theme.css")]
    public async Task<IActionResult> ThemeCss()
    {
        // Always revalidated, never served stale from the browser cache — a
        // publish must be visible on the next normal page load, not only
        // after a hard refresh.
        Response.Headers.CacheControl = "no-cache, must-revalidate";

        // Deliberately not gated on IsResolved — anonymous/pre-login
        // requests fall back to the platform default theme via the resolver
        // rather than being forbidden (see class doc comment).
        await tenantContext.EnsureResolvedAsync();
        var theme = await brandingThemeResolverService.GetActiveThemeAsync(tenantContext.CurrentTenantId);
        return Content(BuildCss(theme), "text/css");
    }

    private IActionResult BackToBranding(string message) =>
        Redirect("/staffops/branding?message=" + Uri.EscapeDataString(message));

    private static string BuildCss(ResolvedBrandingTheme theme)
    {
        var declarations = string.Join(" ", theme.CssVariables.Select(kvp => $"{kvp.Key}: {kvp.Value};"));
        return $":root {{ {declarations} }}";
    }
}
