using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

public interface IBrandingAssetStorageService
{
    /// <summary>
    /// Validates content-type/size, decodes the file to confirm it's a real
    /// image and capture its true dimensions, writes it to disk under a
    /// GUID-derived filename (the original filename is never used for the
    /// storage path), and persists the asset metadata row. Throws
    /// BrandingAssetValidationException on any validation failure.
    /// </summary>
    Task<BrandingAsset> StoreAsync(IFormFile file, BrandingAssetType assetType, int? uploadedByMemberId, Guid tenantId);

    string ResolveUrl(BrandingAsset asset);
}
