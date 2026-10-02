using ProgrammePulse.Services.Localization;

namespace ProgrammePulse.Tests.Localization;

/// <summary>
/// First-visit language from the browser. ASP.NET Core's stock provider needs
/// an exact tag, so a Welsh browser sending "cy" never reached "cy-GB"; these
/// pin the language-level matching that replaced it.
/// </summary>
public class BrowserLanguageRequestCultureProviderTests
{
    private static readonly LocalizationSettings EnglishAndWelsh = new();

    private static readonly LocalizationSettings Global = new()
    {
        UiCultures = ["en-GB", "cy-GB", "fr-FR", "de-DE"],
        FormatCultures = ["en-GB", "cy-GB", "en-US", "fr-FR", "de-DE"]
    };

    [Theory]
    [InlineData("cy", "cy-GB", "cy-GB")]
    [InlineData("cy-GB,cy;q=0.9,en;q=0.8", "cy-GB", "cy-GB")]
    [InlineData("en-GB,en;q=0.9", "en-GB", "en-GB")]
    public void A_welsh_or_english_browser_gets_its_language(string header, string expectedUi, string expectedFormat)
    {
        var match = BrowserLanguageRequestCultureProvider.Match(header, EnglishAndWelsh);

        Assert.Equal((expectedFormat, expectedUi), match);
    }

    [Fact]
    public void A_regional_variant_reaches_the_configured_region_of_its_language()
    {
        Assert.Equal(("fr-FR", "fr-FR"), BrowserLanguageRequestCultureProvider.Match("fr-CA,fr;q=0.9", Global));
    }

    /// <summary>
    /// An American browser reads the English interface but keeps US dates and
    /// numbers when the deployment offers them — language and format are
    /// independent choices.
    /// </summary>
    [Fact]
    public void Formatting_keeps_the_browsers_region_when_it_is_offered()
    {
        Assert.Equal(("en-US", "en-GB"), BrowserLanguageRequestCultureProvider.Match("en-US", Global));
    }

    [Fact]
    public void Preference_order_is_by_quality_not_position()
    {
        Assert.Equal(("de-DE", "de-DE"), BrowserLanguageRequestCultureProvider.Match("ja;q=0.9, de;q=0.95, fr;q=0.5", Global));
    }

    [Theory]
    [InlineData("ja-JP")]              // not offered: fall through to the default
    [InlineData("cy;q=0")]             // explicitly refused
    [InlineData("*")]                  // no preference
    [InlineData("")]
    [InlineData(null)]
    [InlineData(";;;=,,,")]            // garbage is ignored, never an exception
    public void Anything_unsupported_or_malformed_yields_no_match(string? header)
    {
        Assert.Null(BrowserLanguageRequestCultureProvider.Match(header, EnglishAndWelsh));
    }
}
