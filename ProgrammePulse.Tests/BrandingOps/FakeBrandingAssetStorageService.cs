using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// Only ResolveUrl is exercised by BrandingThemeResolverServiceTests; StoreAsync
/// is not used by anything under test here.
/// </summary>
public sealed class FakeBrandingAssetStorageService : IBrandingAssetStorageService
{
    public Task<BrandingAsset> StoreAsync(IFormFile file, BrandingAssetType assetType, int? uploadedByMemberId, Guid tenantId) =>
        throw new NotSupportedException("Not exercised by these tests.");

    public string ResolveUrl(BrandingAsset asset) => asset.StoragePath;
}
