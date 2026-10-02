using System.Globalization;

namespace ProgrammePulse.Services.Localization;

/// <summary>
/// The document-level attributes a page needs for the current interface
/// language: <c>lang</c> for screen readers and hyphenation, and <c>dir</c>
/// so a right-to-left language (Arabic, Hebrew, Urdu...) lays out correctly.
/// One place, so every layout and standalone page agrees.
/// </summary>
public static class CultureMarkup
{
    public static string Lang => CultureInfo.CurrentUICulture.Name;

    public static string Dir => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "rtl" : "ltr";

    /// <summary>
    /// How a language names itself in the switcher ("Cymraeg", "Français").
    /// The bare language name is used unless two configured cultures share a
    /// language, when the region is needed to tell them apart.
    /// </summary>
    public static string NativeLabel(CultureInfo culture, IEnumerable<CultureInfo> offered)
    {
        var shareLanguage = offered.Count(o => o.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName) > 1;
        var name = shareLanguage || culture.Parent.Equals(CultureInfo.InvariantCulture)
            ? culture.NativeName
            : culture.Parent.NativeName;
        return name.Length == 0 ? culture.Name : char.ToUpper(name[0], culture) + name[1..];
    }
}
