using Microsoft.Extensions.Caching.Memory;
using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// Resolves the theme served to the front end. Caches the active (Published)
/// theme per tenant so every page request doesn't hit the database;
/// invalidated explicitly by the controller after publish/rollback, with a
/// short absolute expiry as a safety net rather than the only invalidation
/// path. A null tenantId (anonymous/pre-login request) never touches the
/// cache or the repository at all — it always resolves the platform default.
/// </summary>
public sealed class BrandingThemeResolverService(
    IBrandingRepository brandingRepository,
    IBrandingAssetStorageService assetStorageService,
    IMemoryCache cache,
    Microsoft.Extensions.Options.IOptions<ProductBrandOptions>? productBrand = null) : IBrandingThemeResolverService
{
    private const string CacheKeyPrefix = "branding:active-theme:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<ResolvedBrandingTheme> GetActiveThemeAsync(Guid? tenantId)
    {
        if (tenantId is null)
        {
            return BuildDefaultTheme();
        }

        var cacheKey = CacheKeyPrefix + tenantId;
        if (cache.TryGetValue(cacheKey, out ResolvedBrandingTheme? cached) && cached is not null)
        {
            return cached;
        }

        var published = await brandingRepository.GetActivePublishedAsync(tenantId.Value);
        var resolved = published is null || !published.IsActive
            ? BuildDefaultTheme()
            : await BuildThemeAsync(published, isUsingPlatformDefault: false, tenantId.Value);

        cache.Set(cacheKey, resolved, CacheDuration);
        return resolved;
    }

    public Task<ResolvedBrandingTheme> ResolvePreviewAsync(BrandingProfile profile) =>
        BuildThemeAsync(profile, isUsingPlatformDefault: false, profile.TenantId);

    public void InvalidateCache(Guid tenantId) => cache.Remove(CacheKeyPrefix + tenantId);

    private async Task<ResolvedBrandingTheme> BuildThemeAsync(BrandingProfile profile, bool isUsingPlatformDefault, Guid? tenantId)
    {
        var cssVariables = BrandingCssVariableMapper.Map(
            profile.PrimaryColour,
            profile.SecondaryColour,
            profile.AccentColour,
            profile.TextColour,
            profile.SurfaceColour,
            profile.BackgroundColour,
            profile.BorderRadiusPx,
            profile.FontOption);

        var logoUrl = await ResolveAssetUrlAsync(profile.LogoAssetKey, tenantId);
        var faviconUrl = await ResolveAssetUrlAsync(profile.FaviconAssetKey, tenantId);

        return new ResolvedBrandingTheme
        {
            CssVariables = cssVariables,
            HeaderStyle = profile.HeaderStyle,
            FooterStyle = profile.FooterStyle,
            DarkModeEnabled = profile.DarkModeEnabled,
            LogoUrl = logoUrl,
            FaviconUrl = faviconUrl,
            CompanyName = profile.CompanyName,
            IsUsingPlatformDefault = isUsingPlatformDefault
        };
    }

    private async Task<string?> ResolveAssetUrlAsync(Guid? assetKey, Guid? tenantId)
    {
        if (assetKey is null || tenantId is null)
        {
            return null;
        }

        var asset = await brandingRepository.GetAssetAsync(assetKey.Value, tenantId.Value);
        return asset is null ? null : assetStorageService.ResolveUrl(asset);
    }

    private ResolvedBrandingTheme BuildDefaultTheme()
    {
        var cssVariables = BrandingCssVariableMapper.Map(
            PlatformDefaultTheme.PrimaryColour,
            PlatformDefaultTheme.SecondaryColour,
            PlatformDefaultTheme.AccentColour,
            PlatformDefaultTheme.TextColour,
            PlatformDefaultTheme.SurfaceColour,
            PlatformDefaultTheme.BackgroundColour,
            PlatformDefaultTheme.BorderRadiusPx,
            PlatformDefaultTheme.FontOption);

        return new ResolvedBrandingTheme
        {
            CssVariables = cssVariables,
            HeaderStyle = PlatformDefaultTheme.HeaderStyle,
            FooterStyle = PlatformDefaultTheme.FooterStyle,
            DarkModeEnabled = PlatformDefaultTheme.DarkModeEnabled,
            LogoUrl = null,
            FaviconUrl = null,
            // The configured product name, so a rename never needs a code change.
            CompanyName = productBrand?.Value.Name ?? PlatformDefaultTheme.CompanyName,
            IsUsingPlatformDefault = true
        };
    }
}
