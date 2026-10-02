using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public class MarketConfigurationTests
{
    [Fact]
    public void Language_market_currency_and_format_are_independent()
    {
        var market = new MarketSettings { MarketCode = "IE", UiCulture = "cy-GB", FormatCulture = "en-IE", ReportingCurrency = "USD", TimeZoneId = "Europe/London", FiscalYearStartMonth = 4 };
        MarketConfiguration.Validate(market);
        Assert.Equal("USD", market.ReportingCurrency);
        Assert.Equal("cy-GB", market.UiCulture);
    }

    [Fact]
    public void Unsupported_settings_are_rejected_before_persistence()
    {
        MarketSettings[] invalid = [new() { MarketCode = "XX" }, new() { UiCulture = "fr-FR" },
            new() { FormatCulture = "wrong" }, new() { ReportingCurrency = "XXX" },
            new() { TimeZoneId = "Mars" }, new() { FiscalYearStartMonth = 0 }, new() { FiscalYearStartMonth = 13 }];
        foreach (var settings in invalid) Assert.Throws<ReviewValidationException>(() => MarketConfiguration.Validate(settings));
    }

    [Theory]
    [InlineData("Europe/London", 3, 29, 23)]
    [InlineData("Europe/London", 10, 25, 25)]
    [InlineData("Europe/Dublin", 3, 29, 23)]
    [InlineData("Europe/Dublin", 10, 25, 25)]
    [InlineData("Etc/UTC", 3, 29, 24)]
    public void Half_open_local_days_preserve_DST_duration(string id, int month, int day, int hours)
    {
        var date = new DateOnly(2026, month, day);
        var range = MarketConfiguration.UtcPeriod(date, date.AddDays(1), TimeZoneInfo.FindSystemTimeZoneById(id));
        Assert.Equal(hours, (range.EndUtc - range.StartUtc).TotalHours);
        Assert.Equal(DateTimeKind.Utc, range.StartUtc.Kind);
    }

    [Fact]
    public void Ambiguous_or_missing_midnight_is_never_guessed()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("MidnightTest", TimeSpan.Zero, "MidnightTest", "Standard", "Summer", [rule]);
        foreach (var month in new[] { 3, 10 })
            Assert.Throws<ReviewValidationException>(() => MarketConfiguration.UtcPeriod(new(2026, month, 1), new(2026, month, 2), zone));
    }

    [Theory]
    [InlineData(3, 31, 2025)]
    [InlineData(4, 1, 2026)]
    public void Fiscal_start_is_explicit_not_inferred_from_country(int month, int day, int year)
    {
        var range = MarketConfiguration.FiscalYear(new(2026, month, day), 4);
        Assert.Equal(new DateOnly(year, 4, 1), range.Start);
        Assert.Equal(new DateOnly(year + 1, 4, 1), range.EndExclusive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-culture")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Language_endpoint_rejects_unknown_values_without_setting_a_cookie(string culture)
    {
        var controller = Controller();
        Assert.IsType<BadRequestResult>(controller.Set(culture, "/staffops"));
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Language_switch_preserves_existing_format_preference()
    {
        var controller = Controller();
        controller.Request.Headers.Cookie = ".AspNetCore.Culture=c=en-IE|uic=en-GB";
        controller.Set("cy-GB", null);
        var header = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(Uri.EscapeDataString("c=en-IE|uic=cy-GB"), header);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_format_is_validated_independently_and_query_strings_do_not_override_culture()
    {
        var controller = Controller();
        Assert.IsType<BadRequestResult>(controller.Set("en-GB", null, "invalid"));
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);

        // The member's explicit choice comes first; the browser's language is
        // only a first-visit fallback. No query-string provider, ever: a link
        // must not be able to change someone's language.
        var providers = MarketConfiguration.LocalizationOptions().RequestCultureProviders;
        Assert.IsType<CookieRequestCultureProvider>(providers[0]);
        Assert.IsType<ProgrammePulse.Services.Localization.BrowserLanguageRequestCultureProvider>(providers[1]);
        Assert.Equal(2, providers.Count);
        Assert.DoesNotContain(providers, p => p is QueryStringRequestCultureProvider);
    }

    [Fact]
    public void Browser_language_can_be_switched_off_leaving_only_the_explicit_choice()
    {
        var options = MarketConfiguration.LocalizationOptions(new ProgrammePulse.Services.Localization.LocalizationSettings { UseBrowserLanguage = false });

        Assert.IsType<CookieRequestCultureProvider>(Assert.Single(options.RequestCultureProviders));
    }

    private static LanguageController Controller() => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
}
