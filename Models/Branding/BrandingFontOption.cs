namespace ProgrammePulse.Models.Branding;

/// <summary>
/// Closed set of font choices — never free text. BrandingCssVariableMapper
/// maps each value to a fixed, literal CSS font-stack string, so a font
/// selection can never be used to inject CSS.
/// </summary>
public enum BrandingFontOption
{
    System,
    Serif,
    Mono,
    Rounded
}
