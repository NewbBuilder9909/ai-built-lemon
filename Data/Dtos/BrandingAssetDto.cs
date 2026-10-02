using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class BrandingAssetDto
{
    public const string TableName = "BrandingOps_Asset";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("assetKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid AssetKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("assetType")]
    [Length(16)]
    public string AssetType { get; set; } = null!;

    [Column("fileName")]
    [Length(260)]
    public string FileName { get; set; } = null!;

    [Column("contentType")]
    [Length(100)]
    public string ContentType { get; set; } = null!;

    [Column("sizeBytes")]
    public long SizeBytes { get; set; }

    [Column("widthPx")]
    public int WidthPx { get; set; }

    [Column("heightPx")]
    public int HeightPx { get; set; }

    [Column("storagePath")]
    [Length(400)]
    public string StoragePath { get; set; } = null!;

    [Column("uploadedByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? UploadedByMemberId { get; set; }

    [Column("uploadedAtUtc")]
    public DateTime UploadedAtUtc { get; set; }
}
