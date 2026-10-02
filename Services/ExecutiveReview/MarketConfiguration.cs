using System.Globalization;
using Microsoft.AspNetCore.Localization;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Services.Localization;

namespace ProgrammePulse.Services.ExecutiveReview;

/// <summary>
/// Market, language and format rules. The lists themselves are deployment
/// configuration (<see cref="LocalizationSettings"/>, the "Localization"
/// section); the parameterless overloads use its defaults, which are the
/// original GB/IE, English/Welsh set.
/// </summary>
public static class MarketConfiguration
{
    public static void Validate(MarketSettings settings, LocalizationSettings? localization = null)
    {
        localization ??= new LocalizationSettings();
        if (!localization.SupportedMarkets.Contains(settings.MarketCode) || !localization.SupportedUiCultures.Contains(settings.UiCulture)
            || !localization.SupportedFormatCultures.Contains(settings.FormatCulture) || !localization.SupportedCurrencies.Contains(settings.ReportingCurrency)
            || !localization.SupportedTimeZones.Contains(settings.TimeZoneId) || settings.FiscalYearStartMonth is < 1 or > 12)
            throw new ReviewValidationException("Review.InvalidSettings");
        _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
    }

    /// <summary>
    /// The member's explicit choice (the culture cookie) always wins. Without
    /// one, their browser's language is matched by language, not exact tag,
    /// when <see cref="LocalizationSettings.UseBrowserLanguage"/> is on. There
    /// is deliberately no query-string provider: a link must never change
    /// someone's language.
    /// </summary>
    public static RequestLocalizationOptions LocalizationOptions(LocalizationSettings? localization = null)
    {
        localization ??= new LocalizationSettings();
        var providers = new List<IRequestCultureProvider> { new CookieRequestCultureProvider() };
        if (localization.UseBrowserLanguage)
        {
            providers.Add(new BrowserLanguageRequestCultureProvider(localization));
        }

        return new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(localization.DefaultCulture),
            SupportedCultures = localization.SupportedFormatCultures.Select(CultureInfo.GetCultureInfo).ToArray(),
            SupportedUICultures = localization.SupportedUiCultures.Select(CultureInfo.GetCultureInfo).ToArray(),
            RequestCultureProviders = providers,
            FallBackToParentCultures = false,
            FallBackToParentUICultures = false
        };
    }

    public static (DateOnly Start, DateOnly EndExclusive) FiscalYear(DateOnly date, int startMonth)
    {
        if (startMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(startMonth));
        var start = new DateOnly(date.Month < startMonth ? date.Year - 1 : date.Year, startMonth, 1);
        return (start, start.AddYears(1));
    }

    public static (DateTime StartUtc, DateTime EndUtc) UtcPeriod(DateOnly from, DateOnly toExclusive, TimeZoneInfo zone)
    {
        if (toExclusive <= from) throw new ArgumentException("The end must be after the start.");
        DateTime Convert(DateOnly date)
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // A guessed offset would silently move a reporting boundary.
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                throw new ReviewValidationException("Review.AmbiguousBoundary");
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        return (Convert(from), Convert(toExclusive));
    }
}

public sealed class ReviewValidationException(string resourceKey) : Exception(resourceKey);
public sealed class ReviewConflictException() : Exception("Review.Conflict");

public sealed class ExecutiveReviewOptions
{
    public const string SectionName = "ExecutiveReview";
    public bool Enabled { get; set; }
    public int MaximumPublicationAgeHours { get; set; } = 48;
}
