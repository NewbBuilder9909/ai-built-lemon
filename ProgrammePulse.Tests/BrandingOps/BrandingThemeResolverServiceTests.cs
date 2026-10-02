using Microsoft.Extensions.Caching.Memory;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

public class BrandingThemeResolverServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static BrandingThemeResolverService BuildSut(FakeBrandingRepository repository) =>
        new(repository, new FakeBrandingAssetStorageService(), new MemoryCache(new MemoryCacheOptions()));

    private static BrandingProfile PublishedProfile(string primaryColour = "#123456", Guid? tenantId = null) => new()
    {
        VersionKey = Guid.NewGuid(),
        TenantId = tenantId ?? TenantId,
        CompanyName = "Ostrevane Transit",
        PrimaryColour = primaryColour,
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
        Status = BrandingStatus.Published,
        EffectiveFromUtc = Now,
        CreatedAtUtc = Now
    };

    [Fact]
    public async Task GetActiveThemeAsync_FallsBackToPlatformDefault_WhenNothingPublished()
    {
        var sut = BuildSut(new FakeBrandingRepository());

        var theme = await sut.GetActiveThemeAsync(TenantId);

        Assert.True(theme.IsUsingPlatformDefault);
        Assert.Equal(PlatformDefaultTheme.PrimaryColour, theme.CssVariables["--brand-primary"]);
    }

    [Fact]
    public async Task GetActiveThemeAsync_UsesPublishedProfile_WhenOneExists()
    {
        var repository = new FakeBrandingRepository();
        repository.Profiles.Add(PublishedProfile("#abcdef"));
        var sut = BuildSut(repository);

        var theme = await sut.GetActiveThemeAsync(TenantId);

        Assert.False(theme.IsUsingPlatformDefault);
        Assert.Equal("#abcdef", theme.CssVariables["--brand-primary"]);
    }

    [Fact]
    public async Task GetActiveThemeAsync_IsCached_UntilInvalidated()
    {
        var repository = new FakeBrandingRepository();
        repository.Profiles.Add(PublishedProfile("#111111"));
        var sut = BuildSut(repository);

        var first = await sut.GetActiveThemeAsync(TenantId);

        // Mutate the underlying data without going through publish/rollback.
        repository.Profiles[0] = repository.Profiles[0] with { PrimaryColour = "#222222" };
        var stillCached = await sut.GetActiveThemeAsync(TenantId);

        sut.InvalidateCache(TenantId);
        var afterInvalidate = await sut.GetActiveThemeAsync(TenantId);

        Assert.Equal("#111111", first.CssVariables["--brand-primary"]);
        Assert.Equal("#111111", stillCached.CssVariables["--brand-primary"]);
        Assert.Equal("#222222", afterInvalidate.CssVariables["--brand-primary"]);
    }

    [Fact]
    public async Task ResolvePreviewAsync_ReflectsGivenProfile_NotTheCachedActiveTheme()
    {
        var repository = new FakeBrandingRepository();
        repository.Profiles.Add(PublishedProfile("#111111"));
        var sut = BuildSut(repository);

        await sut.GetActiveThemeAsync(TenantId); // warm the cache with the published theme

        var draft = PublishedProfile("#999999") with { Status = BrandingStatus.Draft };
        var preview = await sut.ResolvePreviewAsync(draft);

        Assert.Equal("#999999", preview.CssVariables["--brand-primary"]);
    }
}
