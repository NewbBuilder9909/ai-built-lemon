using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Models.ViewModels.Branding;

public sealed record BrandingAdminViewModel(
    BrandingProfileFormViewModel Form,
    bool HasPublished,
    bool HasDraft,
    string? Message,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> ValidationWarnings);

public sealed record BrandingProfileFormViewModel(
    string CompanyName,
    string? TenantUiLabel,
    string PrimaryColour,
    string SecondaryColour,
    string AccentColour,
    string TextColour,
    string SurfaceColour,
    string BackgroundColour,
    int BorderRadiusPx,
    BrandingFontOption FontOption,
    BrandingHeaderStyle HeaderStyle,
    BrandingFooterStyle FooterStyle,
    bool DarkModeEnabled,
    bool IsActive,
    string? LogoUrl,
    string? FaviconUrl)
{
    public static BrandingProfileFormViewModel FromProfile(BrandingProfile profile, string? logoUrl, string? faviconUrl) => new(
        profile.CompanyName,
        profile.TenantUiLabel,
        profile.PrimaryColour,
        profile.SecondaryColour,
        profile.AccentColour,
        profile.TextColour,
        profile.SurfaceColour,
        profile.BackgroundColour,
        profile.BorderRadiusPx,
        profile.FontOption,
        profile.HeaderStyle,
        profile.FooterStyle,
        profile.DarkModeEnabled,
        profile.IsActive,
        logoUrl,
        faviconUrl);

    public static BrandingProfileFormViewModel Default(string companyName) => new(
        companyName,
        null,
        PlatformDefaultTheme.PrimaryColour,
        PlatformDefaultTheme.SecondaryColour,
        PlatformDefaultTheme.AccentColour,
        PlatformDefaultTheme.TextColour,
        PlatformDefaultTheme.SurfaceColour,
        PlatformDefaultTheme.BackgroundColour,
        PlatformDefaultTheme.BorderRadiusPx,
        PlatformDefaultTheme.FontOption,
        PlatformDefaultTheme.HeaderStyle,
        PlatformDefaultTheme.FooterStyle,
        PlatformDefaultTheme.DarkModeEnabled,
        true,
        null,
        null);
}
