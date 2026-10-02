using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

public interface IBrandingThemeResolverService
{
    /// <summary>
    /// Resolves the currently active (Published) theme for the given tenant,
    /// cached per-tenant until the next publish/rollback. Falls back to
    /// PlatformDefaultTheme when no profile has ever been published for that
    /// tenant, or when tenantId is null — the pre-login/anonymous case (e.g.
    /// StaffBrandingController.ThemeCss before sign-in), which never touches
    /// the cache or the repository.
    /// </summary>
    Task<ResolvedBrandingTheme> GetActiveThemeAsync(Guid? tenantId);

    /// <summary>
    /// Resolves a specific (typically Draft) profile's tokens without
    /// touching or affecting the cached active theme — used for the admin
    /// preview screen.
    /// </summary>
    Task<ResolvedBrandingTheme> ResolvePreviewAsync(BrandingProfile profile);

    /// <summary>
    /// Clears the cached active theme for the given tenant.
    /// Must be called after publish/rollback.
    /// </summary>
    void InvalidateCache(Guid tenantId);
}
