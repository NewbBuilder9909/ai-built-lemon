using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

public class BrandingCssVariableMapperTests
{
    [Fact]
    public void Map_EmitsOneVariablePerToken_UsingProvidedValues()
    {
        var variables = BrandingCssVariableMapper.Map(
            primaryColour: "#111111",
            secondaryColour: "#222222",
            accentColour: "#333333",
            textColour: "#444444",
            surfaceColour: "#555555",
            backgroundColour: "#666666",
            borderRadiusPx: 12,
            fontOption: BrandingFontOption.Mono);

        Assert.Equal("#111111", variables["--brand-primary"]);
        Assert.Equal("#222222", variables["--brand-secondary"]);
        Assert.Equal("#333333", variables["--brand-accent"]);
        Assert.Equal("#444444", variables["--brand-text"]);
        Assert.Equal("#555555", variables["--brand-surface"]);
        Assert.Equal("#666666", variables["--brand-background"]);
        Assert.Equal("12px", variables["--brand-radius"]);
        Assert.Equal(BrandingCssVariableMapper.ResolveFontStack(BrandingFontOption.Mono), variables["--brand-font-family"]);
    }

    [Theory]
    [InlineData(BrandingFontOption.System)]
    [InlineData(BrandingFontOption.Serif)]
    [InlineData(BrandingFontOption.Mono)]
    [InlineData(BrandingFontOption.Rounded)]
    public void ResolveFontStack_ReturnsALiteralStack_NeverEmpty(BrandingFontOption option)
    {
        var stack = BrandingCssVariableMapper.ResolveFontStack(option);

        Assert.False(string.IsNullOrWhiteSpace(stack));
    }
}
