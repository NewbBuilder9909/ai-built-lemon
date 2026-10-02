namespace ProgrammePulse.Models.Branding;

/// <summary>
/// What IBrandingThemeResolverService hands to the front end: a fixed set of
/// CSS custom properties plus the layout-level choices that aren't
/// expressible as a CSS variable. Every value here has already passed
/// BrandingValidationService — never raw user text.
/// </summary>
public sealed record ResolvedBrandingTheme
{
    public required IReadOnlyDictionary<string, string> CssVariables { get; init; }

    public required BrandingHeaderStyle HeaderStyle { get; init; }

    public required BrandingFooterStyle FooterStyle { get; init; }

    public required bool DarkModeEnabled { get; init; }

    public string? LogoUrl { get; init; }

    public string? FaviconUrl { get; init; }

    public required string CompanyName { get; init; }

    public required bool IsUsingPlatformDefault { get; init; }

    /// <summary>
    /// Changes whenever any published colour, radius or font changes. The
    /// layout puts it in the theme.css address, so a publish is a new URL and
    /// no browser, proxy or back/forward cache can keep the old colours.
    /// </summary>
    public string Version =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join(";", CssVariables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}:{v.Value}")))))[..12]
            .ToLowerInvariant();
}
