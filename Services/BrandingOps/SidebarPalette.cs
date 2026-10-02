using System.Globalization;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// The navigation rail's text colours, derived from the published secondary
/// colour it sits on. The rail used to hardcode light text for a dark rail,
/// so a light secondary colour made the menu unreadable, and changing the
/// brand's text colour never reached it. Light or dark text is chosen by
/// whichever has the higher WCAG contrast against the rail; the muted shade
/// is used only while it still reaches 4.5:1, otherwise it falls back to the
/// full text colour.
/// </summary>
public static class SidebarPalette
{
    public const double MinimumContrast = 4.5;

    private const string LightText = "#e2e8f0";
    private const string LightMuted = "#a7b3c4";
    private const string LightActive = "#ffffff";
    private const string DarkText = "#1e293b";
    private const string DarkMuted = "#475569";
    private const string DarkActive = "#0f172a";

    public static IReadOnlyDictionary<string, string> For(string backgroundHex)
    {
        if (!TryParse(backgroundHex, out var background))
        {
            background = (0x0f, 0x17, 0x2a);
        }

        var light = Contrast((255, 255, 255), background) >= Contrast((0, 0, 0), background);

        var text = light ? LightText : DarkText;
        var active = light ? LightActive : DarkActive;
        var muted = light ? LightMuted : DarkMuted;

        // A mid-tone rail can defeat the softened shades; pure white or black
        // is then the most contrast there is.
        if (Contrast(Parse(text), background) < MinimumContrast)
        {
            text = active = light ? "#ffffff" : "#000000";
        }

        if (Contrast(Parse(muted), background) < MinimumContrast)
        {
            muted = text;
        }

        var overlay = light ? "255, 255, 255" : "15, 23, 42";
        return new Dictionary<string, string>
        {
            ["--brand-sidebar-text"] = text,
            ["--brand-sidebar-muted"] = muted,
            ["--brand-sidebar-active"] = active,
            ["--brand-sidebar-hover-bg"] = $"rgba({overlay}, 0.07)",
            ["--brand-sidebar-active-bg"] = $"rgba({overlay}, 0.12)",
            ["--brand-sidebar-rule"] = $"rgba({overlay}, 0.12)"
        };
    }

    /// <summary>WCAG 2.x contrast ratio between two colours.</summary>
    public static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (int R, int G, int B) Parse(string hex) =>
        TryParse(hex, out var colour) ? colour : throw new FormatException($"Not a #RRGGBB colour: {hex}");

    private static bool TryParse(string? hex, out (int R, int G, int B) colour)
    {
        colour = default;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        colour = ((value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff);
        return true;
    }

    private static double Luminance((int R, int G, int B) colour)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }
}
