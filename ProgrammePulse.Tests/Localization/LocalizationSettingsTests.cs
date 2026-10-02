using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.Localization;

namespace ProgrammePulse.Tests.Localization;

/// <summary>
/// Languages are configuration. These pin the three things that make that
/// safe: a configured list replaces (never merges with) the defaults, a bad
/// entry is caught before startup, and a configured language with no
/// translation file is reported rather than silently shown in English.
/// </summary>
public class LocalizationSettingsTests
{
    private static LocalizationSettings Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return configuration.GetSection(LocalizationSettings.SectionName).Get<LocalizationSettings>() ?? new LocalizationSettings();
    }

    [Fact]
    public void Defaults_are_the_original_english_and_welsh_set()
    {
        var settings = new LocalizationSettings();

        Assert.Equal(["en-GB", "cy-GB"], settings.SupportedUiCultures);
        Assert.Equal(["en-GB", "cy-GB", "en-IE"], settings.SupportedFormatCultures);
        Assert.Equal("en-GB", settings.DefaultCulture);
        Assert.Empty(settings.Validate());
    }

    /// <summary>
    /// The configuration binder appends to a pre-filled collection. If the
    /// defaults lived in the property initialiser, configuring French would
    /// yield English, Welsh, English and French.
    /// </summary>
    [Fact]
    public void A_configured_language_list_replaces_the_defaults_rather_than_merging()
    {
        var settings = Bind(new()
        {
            ["Localization:UiCultures:0"] = "en-GB",
            ["Localization:UiCultures:1"] = "fr-FR",
            ["Localization:UiCultures:2"] = "de-DE"
        });

        Assert.Equal(["en-GB", "fr-FR", "de-DE"], settings.SupportedUiCultures);
    }

    [Fact]
    public void Adding_a_language_needs_no_code_only_valid_configuration()
    {
        var settings = Bind(new()
        {
            ["Localization:UiCultures:0"] = "en-GB",
            ["Localization:UiCultures:1"] = "ar-SA",
            ["Localization:FormatCultures:0"] = "en-GB",
            ["Localization:FormatCultures:1"] = "ar-SA",
            ["Localization:Currencies:0"] = "SAR",
            ["Localization:TimeZones:0"] = "Asia/Riyadh"
        });

        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("Localization:UiCultures:0", "xx-YY", "not a recognised culture")]
    [InlineData("Localization:DefaultCulture", "fr-FR", "must also be listed in UiCultures")]
    [InlineData("Localization:Currencies:0", "pounds", "ISO 4217")]
    [InlineData("Localization:TimeZones:0", "Mars/Olympus_Mons", "not known to this server")]
    public void A_bad_entry_is_reported_in_words_an_operator_can_act_on(string key, string value, string expected)
    {
        var problems = Bind(new() { [key] = value }).Validate();

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_configured_language_without_a_translation_file_is_reported()
    {
        var settings = Bind(new()
        {
            ["Localization:UiCultures:0"] = "en-GB",
            ["Localization:UiCultures:1"] = "cy-GB",
            ["Localization:UiCultures:2"] = "fr-FR"
        });

        var missing = settings.CulturesWithoutTranslations(typeof(SharedResource).Assembly);

        // English is the neutral resource set and Welsh ships a satellite; French has neither.
        Assert.Equal(["fr-FR"], missing);
    }

    /// <summary>
    /// A translation file may be partial — untranslated keys fall back to
    /// English, which is what makes adding a language incremental. What it may
    /// not do is carry keys English doesn't have: those are typos or leftovers
    /// that no view can reach.
    /// </summary>
    [Fact]
    public void No_translation_file_has_keys_the_english_file_lacks()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "Resources");
        var english = Keys(Path.Combine(resources, "SharedResource.resx"));

        foreach (var file in Directory.EnumerateFiles(resources, "SharedResource.*.resx"))
        {
            var orphans = Keys(file).Except(english).ToList();
            Assert.True(orphans.Count == 0, $"{Path.GetFileName(file)} has keys missing from SharedResource.resx: {string.Join(", ", orphans)}");
        }
    }

    /// <summary>
    /// Resource lookup ignores case, so "My work" and "My Work" are one key:
    /// whichever the build keeps wins, and the other translation silently
    /// never shows. That left a Welsh menu heading in English.
    /// </summary>
    [Fact]
    public void No_two_resource_keys_differ_only_by_case()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "Resources"), "SharedResource*.resx"))
        {
            var collisions = XDocument.Load(file).Root!.Elements("data")
                .Select(e => e.Attribute("name")!.Value)
                .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(g => string.Join(" / ", g))
                .ToList();

            Assert.True(collisions.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", collisions)}");
        }
    }

    [Theory]
    [InlineData("en-GB", new[] { "en-GB", "cy-GB" }, "English")]
    [InlineData("cy-GB", new[] { "en-GB", "cy-GB" }, "Cymraeg")]
    [InlineData("fr-FR", new[] { "en-GB", "fr-FR" }, "Français")]
    public void Each_language_is_named_in_its_own_language(string culture, string[] offered, string expected) =>
        Assert.Equal(expected, CultureMarkup.NativeLabel(CultureInfo.GetCultureInfo(culture), offered.Select(CultureInfo.GetCultureInfo)));

    [Fact]
    public void Two_regions_of_one_language_are_told_apart()
    {
        var offered = new[] { "en-GB", "en-US" }.Select(CultureInfo.GetCultureInfo).ToList();

        var labels = offered.Select(c => CultureMarkup.NativeLabel(c, offered)).ToList();

        Assert.Equal(2, labels.Distinct().Count());
        Assert.All(labels, l => Assert.StartsWith("English (", l));
    }

    private static HashSet<string> Keys(string path) =>
        XDocument.Load(path).Root!.Elements("data").Select(e => e.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgrammePulse.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
