using System.Text.RegularExpressions;
using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// The single gate every branding token passes through before it reaches
/// storage or the CSS mapper. Hex colours, the font/header/footer enums, the
/// radius range, and free-text length/control-character checks are hard
/// failures. Contrast is a warning only — see BrandingProfile doc notes.
/// </summary>
public sealed partial class BrandingValidationService : IBrandingValidationService
{
    private const int MinBorderRadiusPx = 0;
    private const int MaxBorderRadiusPx = 32;
    private const int MaxCompanyNameLength = 200;
    private const int MaxUiLabelLength = 200;
    private const double MinAcceptableContrastRatio = 4.5;

    public BrandingValidationResult Validate(BrandingProfile candidate)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateHexColour(candidate.PrimaryColour, "Primary colour", errors);
        ValidateHexColour(candidate.SecondaryColour, "Secondary colour", errors);
        ValidateHexColour(candidate.AccentColour, "Accent colour", errors);
        ValidateHexColour(candidate.TextColour, "Text colour", errors);
        ValidateHexColour(candidate.SurfaceColour, "Surface colour", errors);
        ValidateHexColour(candidate.BackgroundColour, "Background colour", errors);

        if (candidate.BorderRadiusPx < MinBorderRadiusPx || candidate.BorderRadiusPx > MaxBorderRadiusPx)
        {
            errors.Add($"Border radius must be between {MinBorderRadiusPx} and {MaxBorderRadiusPx}px.");
        }

        if (!Enum.IsDefined(candidate.FontOption))
        {
            errors.Add("Unsupported font option.");
        }

        if (!Enum.IsDefined(candidate.HeaderStyle))
        {
            errors.Add("Unsupported header style.");
        }

        if (!Enum.IsDefined(candidate.FooterStyle))
        {
            errors.Add("Unsupported footer style.");
        }

        ValidateFreeText(candidate.CompanyName, "Company name", MaxCompanyNameLength, required: true, errors);
        ValidateFreeText(candidate.TenantUiLabel, "UI label", MaxUiLabelLength, required: false, errors);

        if (errors.Count == 0)
        {
            CheckContrast(candidate.TextColour, candidate.SurfaceColour, "text and surface", warnings);
            CheckContrast(candidate.TextColour, candidate.BackgroundColour, "text and background", warnings);
        }

        return errors.Count == 0
            ? BrandingValidationResult.Success(warnings)
            : BrandingValidationResult.Failure(errors, warnings);
    }

    private static void ValidateHexColour(string value, string fieldName, List<string> errors)
    {
        if (!HexColourRegex().IsMatch(value))
        {
            errors.Add($"{fieldName} must be a 6-digit hex colour (e.g. #6c5ce7).");
        }
    }

    private static void ValidateFreeText(string? value, string fieldName, int maxLength, bool required, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{fieldName} is required.");
            }
            return;
        }

        if (value.Length > maxLength)
        {
            errors.Add($"{fieldName} must be {maxLength} characters or fewer.");
        }

        if (value.Any(char.IsControl))
        {
            errors.Add($"{fieldName} contains invalid characters.");
        }
    }

    private static void CheckContrast(string foregroundHex, string backgroundHex, string pairName, List<string> warnings)
    {
        var ratio = ComputeContrastRatio(foregroundHex, backgroundHex);
        if (ratio < MinAcceptableContrastRatio)
        {
            warnings.Add($"Contrast between {pairName} is {ratio:0.00}:1, below the recommended {MinAcceptableContrastRatio:0.0}:1 (WCAG AA).");
        }
    }

    private static double ComputeContrastRatio(string hexA, string hexB)
    {
        var lumA = RelativeLuminance(hexA);
        var lumB = RelativeLuminance(hexB);
        var lighter = Math.Max(lumA, lumB);
        var darker = Math.Min(lumA, lumB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
    }

    private static (double R, double G, double B) ParseHex(string hex)
    {
        var value = hex.TrimStart('#');
        var r = Convert.ToInt32(value[..2], 16) / 255.0;
        var g = Convert.ToInt32(value[2..4], 16) / 255.0;
        var b = Convert.ToInt32(value[4..6], 16) / 255.0;
        return (r, g, b);
    }

    private static double ToLinear(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColourRegex();
}
