using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.Branding;
using SixLabors.ImageSharp;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// Azure Blob-backed asset storage with a local file fallback for tests and
/// local development. No SVG (script injection risk) — raster only, and every
/// upload is decoded via ImageSharp and re-encoded to a memory stream rather
/// than the raw bytes being saved verbatim, so a spoofed-extension or polyglot
/// file fails to decode (rejected) instead of being written as-is.
/// </summary>
public sealed class BrandingAssetStorageService(
    IWebHostEnvironment webHostEnvironment,
    IBrandingRepository brandingRepository,
    TimeProvider timeProvider) : IBrandingAssetStorageService
{
    private const string RelativeStorageDirectory = "media/branding";
    private const int MinDimensionPx = 16;
    private const int MaxDimensionPx = 4096;

    private static readonly IReadOnlyDictionary<BrandingAssetType, (long MaxSizeBytes, string[] AllowedContentTypes)> Rules =
        new Dictionary<BrandingAssetType, (long, string[])>
        {
            [BrandingAssetType.Logo] = (2 * 1024 * 1024, ["image/png", "image/jpeg"]),
            [BrandingAssetType.Favicon] = (512 * 1024, ["image/png"]),
            [BrandingAssetType.Hero] = (4 * 1024 * 1024, ["image/png", "image/jpeg"])
        };

    public async Task<BrandingAsset> StoreAsync(IFormFile file, BrandingAssetType assetType, int? uploadedByMemberId, Guid tenantId)
    {
        if (file.Length == 0)
        {
            throw new BrandingAssetValidationException("The uploaded file is empty.");
        }

        var rule = Rules[assetType];
        if (file.Length > rule.MaxSizeBytes)
        {
            throw new BrandingAssetValidationException($"File exceeds the {rule.MaxSizeBytes / 1024}KB limit for this asset type.");
        }

        if (!rule.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new BrandingAssetValidationException($"Unsupported file type '{file.ContentType}'. Allowed: {string.Join(", ", rule.AllowedContentTypes)}.");
        }

        Image image;
        await using (var stream = file.OpenReadStream())
        {
            try
            {
                image = await Image.LoadAsync(stream);
            }
            catch (UnknownImageFormatException)
            {
                throw new BrandingAssetValidationException("The uploaded file is not a valid image.");
            }
        }

        using (image)
        {
            var format = image.Metadata.DecodedImageFormat;
            if (format is null)
            {
                throw new BrandingAssetValidationException("The uploaded file is not a valid image.");
            }

            if (image.Width < MinDimensionPx || image.Height < MinDimensionPx ||
                image.Width > MaxDimensionPx || image.Height > MaxDimensionPx)
            {
                throw new BrandingAssetValidationException(
                    $"Image dimensions must be between {MinDimensionPx}px and {MaxDimensionPx}px on each side.");
            }

            var extension = format.FileExtensions.FirstOrDefault() ?? "png";
            var assetKey = Guid.NewGuid();
            var storedFileName = $"{assetKey:N}.{extension}";
            var tenantRelativeDirectory = $"{RelativeStorageDirectory}/{tenantId:N}";
            var blobPath = $"{tenantRelativeDirectory}/{storedFileName}";

            string storagePath;
            if (TryResolveBlobClient(out var containerClient))
            {
                var blobClient = containerClient.GetBlobClient(blobPath);
                await using var payload = new MemoryStream();
                await image.SaveAsync(payload, format);
                payload.Position = 0;
                await blobClient.UploadAsync(payload, overwrite: true);
                storagePath = blobClient.Uri.AbsoluteUri;
            }
            else
            {
                var storageDirectory = Path.Combine(webHostEnvironment.WebRootPath, tenantRelativeDirectory);
                Directory.CreateDirectory(storageDirectory);
                var absolutePath = Path.Combine(storageDirectory, storedFileName);
                await image.SaveAsync(absolutePath);
                storagePath = $"/{tenantRelativeDirectory}/{storedFileName}";
            }

            var asset = new BrandingAsset
            {
                AssetKey = assetKey,
                TenantId = tenantId,
                AssetType = assetType,
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                WidthPx = image.Width,
                HeightPx = image.Height,
                StoragePath = storagePath,
                UploadedByMemberId = uploadedByMemberId,
                UploadedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

            return await brandingRepository.SaveAssetAsync(asset, tenantId);
        }
    }

    public string ResolveUrl(BrandingAsset asset) => asset.StoragePath;

    private static bool TryResolveBlobClient(out BlobContainerClient containerClient)
    {
        var connectionString = GetSetting("AzureStorage:ConnectionString", "AzureStorage__ConnectionString", "BlobStorage:ConnectionString", "BlobStorage__ConnectionString", "Branding:BlobConnectionString", "Branding__BlobConnectionString");
        var containerName = GetSetting("AzureStorage:ContainerName", "AzureStorage__ContainerName", "BlobStorage:ContainerName", "BlobStorage__ContainerName", "Branding:BlobContainerName", "Branding__BlobContainerName");

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
        {
            containerClient = null!;
            return false;
        }

        containerClient = new BlobContainerClient(connectionString, containerName);
        return true;
    }

    private static string? GetSetting(params string[] keys)
    {
        foreach (var key in keys)
        {
            var alternatives = new[]
            {
                key,
                key.Replace(':', '_'),
                key.Replace(':', '_').Replace("__", "_"),
                key.Replace(':', '_').Replace("__", "_").ToUpperInvariant(),
                key.Replace(':', '_').Replace("__", "_").Replace("-", "_").ToUpperInvariant(),
                key.Replace(':', '_').Replace("__", "_").Replace("-", "_")
            };

            foreach (var alternative in alternatives.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var value = Environment.GetEnvironmentVariable(alternative);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
