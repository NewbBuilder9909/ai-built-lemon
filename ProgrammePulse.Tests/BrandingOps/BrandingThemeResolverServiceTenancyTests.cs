using Microsoft.Extensions.Caching.Memory;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// Branding Ops is the pilot for real tenant data isolation (see
/// docs/tenancy.md). These tests exist specifically to catch the risk
/// called out in IBrandingRepository's doc comments: a missing tenant
/// filter silently leaking one tenant's branding into another's, or a
/// repository/cache call happening at all for an unresolved (anonymous)
/// tenant.
/// </summary>
public class BrandingThemeResolverServiceTenancyTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BrandingThemeResolverService BuildSut(FakeBrandingRepository repository) =>
        new(repository, new FakeBrandingAssetStorageService(), new MemoryCache(new MemoryCacheOptions()));

    private static BrandingProfile PublishedProfile(Guid tenantId, string primaryColour) => new()
    {
        VersionKey = Guid.NewGuid(),
        TenantId = tenantId,
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
    public async Task GetActiveThemeAsync_DoesNotLeakOneTenantsPublishedThemeToAnother()
    {
        var repository = new FakeBrandingRepository();
        repository.Profiles.Add(PublishedProfile(TenantA, "#abcdef"));
        var sut = BuildSut(repository);

        var tenantBTheme = await sut.GetActiveThemeAsync(TenantB);

        Assert.True(tenantBTheme.IsUsingPlatformDefault);
        Assert.Equal(PlatformDefaultTheme.PrimaryColour, tenantBTheme.CssVariables["--brand-primary"]);
    }

    [Fact]
    public async Task GetActiveThemeAsync_CachesSeparately_PerTenant()
    {
        var repository = new FakeBrandingRepository();
        repository.Profiles.Add(PublishedProfile(TenantA, "#111111"));
        repository.Profiles.Add(PublishedProfile(TenantB, "#222222"));
        var sut = BuildSut(repository);

        var tenantATheme = await sut.GetActiveThemeAsync(TenantA);
        var tenantBTheme = await sut.GetActiveThemeAsync(TenantB);

        Assert.Equal("#111111", tenantATheme.CssVariables["--brand-primary"]);
        Assert.Equal("#222222", tenantBTheme.CssVariables["--brand-primary"]);

        sut.InvalidateCache(TenantA);
        var tenantBThemeAfterOtherInvalidate = await sut.GetActiveThemeAsync(TenantB);
        Assert.Equal("#222222", tenantBThemeAfterOtherInvalidate.CssVariables["--brand-primary"]);
    }

    [Fact]
    public async Task GetActiveThemeAsync_WithNullTenant_NeverCallsRepository_AndReturnsPlatformDefault()
    {
        var repository = new SpyBrandingRepository();
        var sut = new BrandingThemeResolverService(repository, new FakeBrandingAssetStorageService(), new MemoryCache(new MemoryCacheOptions()));

        var theme = await sut.GetActiveThemeAsync(null);

        Assert.True(theme.IsUsingPlatformDefault);
        Assert.False(repository.WasQueried);
    }

    /// <summary>
    /// Implements IBrandingRepository directly (rather than wrapping
    /// FakeBrandingRepository, which is sealed) purely to observe whether
    /// any method was called at all.
    /// </summary>
    private sealed class SpyBrandingRepository : IBrandingRepository
    {
        public bool WasQueried { get; private set; }

        public Task<BrandingProfile?> GetActivePublishedAsync(Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult<BrandingProfile?>(null);
        }

        public Task<BrandingProfile?> GetDraftAsync(Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult<BrandingProfile?>(null);
        }

        public Task<IReadOnlyList<BrandingProfile>> GetHistoryAsync(Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult<IReadOnlyList<BrandingProfile>>([]);
        }

        public Task<BrandingProfile> SaveDraftAsync(BrandingProfile draft, Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult(draft);
        }

        public Task<BrandingProfile> PublishAsync(int? actorMemberId, Guid tenantId)
        {
            WasQueried = true;
            throw new InvalidOperationException("Not exercised by this test.");
        }

        public Task<BrandingProfile> RollbackAsync(Guid targetVersionKey, int? actorMemberId, Guid tenantId)
        {
            WasQueried = true;
            throw new InvalidOperationException("Not exercised by this test.");
        }

        public Task<BrandingAsset> SaveAssetAsync(BrandingAsset asset, Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult(asset);
        }

        public Task<BrandingAsset?> GetAssetAsync(Guid assetKey, Guid tenantId)
        {
            WasQueried = true;
            return Task.FromResult<BrandingAsset?>(null);
        }
    }
}
