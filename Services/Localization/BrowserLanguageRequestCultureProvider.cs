using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;

namespace ProgrammePulse.Services.Localization;

/// <summary>
/// Picks a first-visit language from the browser's Accept-Language header,
/// matching by language rather than by exact tag. ASP.NET Core's own
/// AcceptLanguageHeaderRequestCultureProvider needs an exact match (or a
/// parent), so a browser sending "cy" or "fr-CA" would never reach a
/// configured "cy-GB" or "fr-FR" and would silently get English.
///
/// Runs after the culture cookie, so a member's explicit choice always wins.
/// Header values only ever select from the configured lists — nothing a
/// request sends can introduce a culture the deployment doesn't offer.
/// </summary>
public sealed class BrowserLanguageRequestCultureProvider(LocalizationSettings settings) : RequestCultureProvider
{
    // Same bound ASP.NET Core applies: a long header is not worth parsing in full.
    private const int MaximumTagsToTry = 5;

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var match = Match(httpContext.Request.Headers.AcceptLanguage.ToString(), settings);
        return match is null
            ? NullProviderCultureResult
            : Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(match.Value.FormatCulture, match.Value.UiCulture));
    }

    /// <summary>
    /// The best supported (format, UI) pair for an Accept-Language header, or
    /// null when nothing matches. Language is matched on the primary subtag
    /// ("fr" in "fr-CA"); formatting keeps the browser's exact region when the
    /// deployment offers it ("en-US" dates for an American browser even though
    /// the interface text is "en-GB"), and otherwise follows the language.
    /// </summary>
    public static (string FormatCulture, string UiCulture)? Match(string? acceptLanguage, LocalizationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)
            || !StringWithQualityHeaderValue.TryParseList(acceptLanguage.Split(','), out var parsed))
        {
            return null;
        }

        var requested = parsed
            .Where(v => v.Quality is null or > 0)
            .Select((v, index) => (Tag: v.Value.ToString(), Quality: v.Quality ?? 1, index))
            .Where(v => v.Tag != "*")
            .OrderByDescending(v => v.Quality)
            .ThenBy(v => v.index)
            .Take(MaximumTagsToTry)
            .Select(v => v.Tag)
            .ToList();

        foreach (var tag in requested)
        {
            var ui = settings.SupportedUiCultures.FirstOrDefault(c => string.Equals(c, tag, StringComparison.OrdinalIgnoreCase))
                ?? settings.SupportedUiCultures.FirstOrDefault(c => string.Equals(PrimaryLanguage(c), PrimaryLanguage(tag), StringComparison.OrdinalIgnoreCase));
            if (ui is null)
            {
                continue;
            }

            var format = settings.SupportedFormatCultures.FirstOrDefault(c => string.Equals(c, tag, StringComparison.OrdinalIgnoreCase))
                ?? settings.SupportedFormatCultures.FirstOrDefault(c => string.Equals(c, ui, StringComparison.OrdinalIgnoreCase))
                ?? settings.DefaultCulture;
            return (format, ui);
        }

        return null;
    }

    // String parsing, not CultureInfo: the header is untrusted input.
    private static string PrimaryLanguage(string tag)
    {
        var dash = tag.IndexOf('-');
        return dash < 0 ? tag : tag[..dash];
    }
}
