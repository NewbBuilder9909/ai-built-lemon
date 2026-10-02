namespace ProgrammePulse.Models.Branding;

/// <summary>
/// The theme served when no BrandingProfile has ever been published. Values
/// are the existing --ops-* palette from wwwroot/css/app.css,
/// so the fallback matches how the app already looks rather than inventing a
/// new palette.
/// </summary>
public static class PlatformDefaultTheme
{
    /// <summary>
    /// Compile-time fallback only. The deployed name is configuration
    /// (Product:Name, <see cref="ProductBrandOptions"/>) — read that, not this.
    /// </summary>
    public const string CompanyName = "Delivery Evidence Check";
    public const string PrimaryColour = "#2554c7";
    public const string SecondaryColour = "#0f172a";
    public const string AccentColour = "#2554c7";
    public const string TextColour = "#0f172a";
    public const string SurfaceColour = "#ffffff";
    public const string BackgroundColour = "#f4f6f9";
    public const int BorderRadiusPx = 8;
    public const BrandingFontOption FontOption = BrandingFontOption.System;
    public const BrandingHeaderStyle HeaderStyle = BrandingHeaderStyle.Standard;
    public const BrandingFooterStyle FooterStyle = BrandingFooterStyle.Standard;
    public const bool DarkModeEnabled = false;
}
