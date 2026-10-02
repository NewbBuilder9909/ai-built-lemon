using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// The navigation rail's text follows the published secondary colour it sits
/// on. It used to be hardcoded light, so a light secondary colour made the
/// menu unreadable and a brand change never reached it.
/// </summary>
public class SidebarPaletteTests
{
    [Theory]
    [InlineData("#0f172a", "#e2e8f0")]
    [InlineData("#1e3a8a", "#e2e8f0")]
    [InlineData("#ffffff", "#1e293b")]
    [InlineData("#fde68a", "#1e293b")]
    public void Picks_light_text_on_a_dark_rail_and_dark_text_on_a_light_one(string rail, string expectedText)
    {
        Assert.Equal(expectedText, SidebarPalette.For(rail)["--brand-sidebar-text"]);
    }

    [Fact]
    public void Text_and_muted_text_reach_four_and_a_half_to_one_on_every_rail_colour()
    {
        for (var r = 0; r <= 255; r += 17)
        for (var g = 0; g <= 255; g += 17)
        for (var b = 0; b <= 255; b += 17)
        {
            var rail = $"#{r:x2}{g:x2}{b:x2}";
            var palette = SidebarPalette.For(rail);
            var background = SidebarPalette.Parse(rail);

            Assert.True(SidebarPalette.Contrast(SidebarPalette.Parse(palette["--brand-sidebar-text"]), background) >= SidebarPalette.MinimumContrast - 0.01
                     || BestPossible(background) < SidebarPalette.MinimumContrast,
                $"Text on {rail} is below 4.5:1.");
            Assert.True(SidebarPalette.Contrast(SidebarPalette.Parse(palette["--brand-sidebar-muted"]), background)
                        >= Math.Min(SidebarPalette.MinimumContrast, SidebarPalette.Contrast(SidebarPalette.Parse(palette["--brand-sidebar-text"]), background)) - 0.01,
                $"Muted text on {rail} is below 4.5:1 while full text would pass.");
        }
    }

    [Fact]
    public void The_mapper_sends_the_sidebar_tokens_with_the_brand_tokens()
    {
        var variables = BrandingCssVariableMapper.Map("#111111", "#ffffff", "#2554c7", "#0f172a", "#ffffff", "#f4f6f9", 8, BrandingFontOption.System);

        Assert.Equal("#1e293b", variables["--brand-sidebar-text"]);
        Assert.Contains("--brand-sidebar-active-bg", variables.Keys);
    }

    [Fact]
    public void The_theme_version_changes_when_any_published_value_changes()
    {
        static ResolvedBrandingTheme Theme(string secondary) => new()
        {
            CssVariables = BrandingCssVariableMapper.Map("#111111", secondary, "#2554c7", "#0f172a", "#ffffff", "#f4f6f9", 8, BrandingFontOption.System),
            HeaderStyle = default,
            FooterStyle = default,
            DarkModeEnabled = false,
            CompanyName = "Org",
            IsUsingPlatformDefault = false
        };

        Assert.Equal(Theme("#0f172a").Version, Theme("#0f172a").Version);
        Assert.NotEqual(Theme("#0f172a").Version, Theme("#0f172b").Version);
    }

    private static double BestPossible((int R, int G, int B) background) =>
        Math.Max(SidebarPalette.Contrast((255, 255, 255), background), SidebarPalette.Contrast((0, 0, 0), background));
}
