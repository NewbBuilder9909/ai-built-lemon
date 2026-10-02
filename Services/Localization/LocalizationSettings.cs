using System.Globalization;
using System.Reflection;

namespace ProgrammePulse.Services.Localization;

/// <summary>
/// Which languages, formats and markets this deployment offers — bound from
/// the "Localization" configuration section, so adding a language is a
/// settings change plus a translation file, never a code change. See
/// docs/localization.md.
///
/// Arrays are nullable with defaults applied on read, not in initialisers:
/// the configuration binder appends to a pre-filled collection rather than
/// replacing it, so a default list here would silently merge with the
/// configured one.
/// </summary>
public sealed class LocalizationSettings
{
    public const string SectionName = "Localization";

    private static readonly string[] DefaultUiCultureList = ["en-GB", "cy-GB"];
    private static readonly string[] DefaultFormatCultureList = ["en-GB", "cy-GB", "en-IE"];
    private static readonly string[] DefaultMarketList = ["GB", "IE"];
    private static readonly string[] DefaultCurrencyList = ["GBP", "EUR", "USD"];
    private static readonly string[] DefaultTimeZoneList = ["Europe/London", "Europe/Dublin", "Etc/UTC"];

    /// <summary>Used when neither the member's choice nor their browser matches a supported language.</summary>
    public string DefaultCulture { get; set; } = "en-GB";

    /// <summary>Interface languages offered in the switcher. Each needs Resources/SharedResource.{culture}.resx to show translated text.</summary>
    public string[]? UiCultures { get; set; }

    /// <summary>Number, date and currency formats a member may choose, independent of language.</summary>
    public string[]? FormatCultures { get; set; }

    /// <summary>
    /// When the member hasn't chosen a language, use the best match from their
    /// browser's Accept-Language. An explicit choice (the culture cookie) always
    /// wins, and a query string never changes the language.
    /// </summary>
    public bool UseBrowserLanguage { get; set; } = true;

    /// <summary>Market codes a tenant may pick for executive review settings.</summary>
    public string[]? Markets { get; set; }

    /// <summary>ISO 4217 reporting currencies a tenant may pick.</summary>
    public string[]? Currencies { get; set; }

    /// <summary>IANA time zones a tenant may pick for reporting boundaries.</summary>
    public string[]? TimeZones { get; set; }

    public IReadOnlyList<string> SupportedUiCultures => UiCultures is { Length: > 0 } ? UiCultures : DefaultUiCultureList;
    public IReadOnlyList<string> SupportedFormatCultures => FormatCultures is { Length: > 0 } ? FormatCultures : DefaultFormatCultureList;
    public IReadOnlyList<string> SupportedMarkets => Markets is { Length: > 0 } ? Markets : DefaultMarketList;
    public IReadOnlyList<string> SupportedCurrencies => Currencies is { Length: > 0 } ? Currencies : DefaultCurrencyList;
    public IReadOnlyList<string> SupportedTimeZones => TimeZones is { Length: > 0 } ? TimeZones : DefaultTimeZoneList;

    /// <summary>
    /// Every configuration mistake, in words an operator can act on. Program.cs
    /// refuses to start when this is non-empty, rather than serving a language
    /// switcher with a broken entry or a report in an unknown time zone.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var culture in SupportedUiCultures.Concat(SupportedFormatCultures).Append(DefaultCulture).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsKnownCulture(culture))
            {
                problems.Add($"'{culture}' is not a recognised culture name (expected e.g. 'fr-FR').");
            }
        }

        if (!SupportedUiCultures.Contains(DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"DefaultCulture '{DefaultCulture}' must also be listed in UiCultures.");
        }

        if (!SupportedFormatCultures.Contains(DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"DefaultCulture '{DefaultCulture}' must also be listed in FormatCultures.");
        }

        foreach (var currency in SupportedCurrencies.Where(c => c.Length != 3 || !c.All(char.IsAsciiLetterUpper)))
        {
            problems.Add($"Currency '{currency}' is not a three-letter ISO 4217 code.");
        }

        foreach (var zone in SupportedTimeZones)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            {
                problems.Add($"Time zone '{zone}' is not known to this server.");
            }
        }

        return problems;
    }

    /// <summary>
    /// Configured interface languages with no compiled translation file. They
    /// still work — every string falls back to English — so this is a startup
    /// warning, not an error: it tells the operator the switcher offers a
    /// language the screens don't speak yet.
    /// </summary>
    public IReadOnlyList<string> CulturesWithoutTranslations(Assembly resourceAssembly, string neutralLanguage = "en")
    {
        var missing = new List<string>();
        foreach (var name in SupportedUiCultures)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            if (string.Equals(culture.TwoLetterISOLanguageName, neutralLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!HasSatellite(resourceAssembly, culture) && !HasSatellite(resourceAssembly, culture.Parent))
            {
                missing.Add(name);
            }
        }

        return missing;
    }

    private static bool HasSatellite(Assembly assembly, CultureInfo culture)
    {
        if (culture.Equals(CultureInfo.InvariantCulture))
        {
            return false;
        }

        try
        {
            assembly.GetSatelliteAssembly(culture);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static bool IsKnownCulture(string name)
    {
        try
        {
            // predefinedOnly: an unknown tag such as "xx-YY" would otherwise be
            // accepted as a made-up culture with invariant formatting.
            return !string.IsNullOrWhiteSpace(name) && CultureInfo.GetCultureInfo(name, predefinedOnly: true) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
