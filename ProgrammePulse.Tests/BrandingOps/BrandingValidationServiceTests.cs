using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

public class BrandingValidationServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BrandingProfile ValidProfile() => new()
    {
        VersionKey = Guid.NewGuid(),
        CompanyName = "Ostrevane Transit",
        TenantUiLabel = "Ops",
        PrimaryColour = "#6c5ce7",
        SecondaryColour = "#1c1a2b",
        AccentColour = "#6c5ce7",
        TextColour = "#201e33",
        SurfaceColour = "#ffffff",
        BackgroundColour = "#f6f7fb",
        BorderRadiusPx = 10,
        FontOption = BrandingFontOption.System,
        HeaderStyle = BrandingHeaderStyle.Standard,
        FooterStyle = BrandingFooterStyle.Standard,
        DarkModeEnabled = false,
        IsActive = true,
        Status = BrandingStatus.Draft,
        EffectiveFromUtc = Now,
        CreatedAtUtc = Now
    };

    [Fact]
    public void Validate_AcceptsWellFormedProfile()
    {
        var result = new BrandingValidationService().Validate(ValidProfile());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("6c5ce7")]
    [InlineData("#6c5ce")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void Validate_RejectsMalformedHexColour(string badHex)
    {
        var profile = ValidProfile() with { PrimaryColour = badHex };

        var result = new BrandingValidationService().Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Primary colour"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void Validate_RejectsBorderRadiusOutsideRange(int radius)
    {
        var profile = ValidProfile() with { BorderRadiusPx = radius };

        var result = new BrandingValidationService().Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Border radius"));
    }

    [Fact]
    public void Validate_RejectsUnsupportedFontOption()
    {
        var profile = ValidProfile() with { FontOption = (BrandingFontOption)999 };

        var result = new BrandingValidationService().Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("font"));
    }

    [Fact]
    public void Validate_RequiresCompanyName()
    {
        var profile = ValidProfile() with { CompanyName = "   " };

        var result = new BrandingValidationService().Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Company name"));
    }

    [Fact]
    public void Validate_WarnsOnLowContrastButStaysValid()
    {
        // Near-white text on a near-white surface: legal hex, unreadable pairing.
        var profile = ValidProfile() with { TextColour = "#fefefe", SurfaceColour = "#ffffff", BackgroundColour = "#ffffff" };

        var result = new BrandingValidationService().Validate(profile);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }
}
