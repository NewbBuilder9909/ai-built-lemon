using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

public interface IBrandingValidationService
{
    /// <summary>
    /// Hard-fails on invalid hex colours, unsupported font/header/footer
    /// values, an out-of-range border radius, or malformed free text. Contrast
    /// readability between text and surface/background colours is checked
    /// but only ever added as a warning — it doesn't block a save.
    /// </summary>
    BrandingValidationResult Validate(BrandingProfile candidate);
}
