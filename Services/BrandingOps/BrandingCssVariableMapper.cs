using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// Stateless mapping from validated tokens to CSS custom properties. Every
/// output value is either an already hex-validated colour string, a clamped
/// integer with a fixed unit suffix, or one of the hardcoded font-stack
/// literals below — never raw user text — which is what makes "no CSS
/// injection" actually true rather than merely intended.
/// </summary>
public static class BrandingCssVariableMapper
{
    public static IReadOnlyDictionary<string, string> Map(
        string primaryColour,
        string secondaryColour,
        string accentColour,
        string textColour,
        string surfaceColour,
        string backgroundColour,
        int borderRadiusPx,
        BrandingFontOption fontOption)
    {
        var variables = new Dictionary<string, string>
        {
            ["--brand-primary"] = primaryColour,
            ["--brand-secondary"] = secondaryColour,
            ["--brand-accent"] = accentColour,
            ["--brand-text"] = textColour,
            ["--brand-surface"] = surfaceColour,
            ["--brand-background"] = backgroundColour,
            ["--brand-radius"] = $"{borderRadiusPx}px",
            ["--brand-font-family"] = ResolveFontStack(fontOption)
        };

        // The rail sits on the secondary colour, so its text follows that,
        // not the content text colour (which is chosen for the light page).
        foreach (var (name, value) in SidebarPalette.For(secondaryColour))
        {
            variables[name] = value;
        }

        return variables;
    }

    public static string ResolveFontStack(BrandingFontOption option) => option switch
    {
        BrandingFontOption.System => "-apple-system, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif",
        BrandingFontOption.Serif => "\"Iowan Old Style\", Georgia, Cambria, \"Times New Roman\", Times, serif",
        BrandingFontOption.Mono => "\"SFMono-Regular\", Consolas, \"Liberation Mono\", Menlo, monospace",
        BrandingFontOption.Rounded => "Nunito, \"Segoe UI\", Verdana, sans-serif",
        _ => "-apple-system, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif"
    };
}
