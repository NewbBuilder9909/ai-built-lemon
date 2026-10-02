namespace ProgrammePulse.Models.Branding;

public sealed record BrandingAsset
{
    public required Guid AssetKey { get; init; }

    public Guid? TenantId { get; init; }

    public required BrandingAssetType AssetType { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required int WidthPx { get; init; }

    public required int HeightPx { get; init; }

    public required string StoragePath { get; init; }

    public int? UploadedByMemberId { get; init; }

    public required DateTime UploadedAtUtc { get; init; }
}
