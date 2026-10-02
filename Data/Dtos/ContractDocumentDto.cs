using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ContractDocumentDto
{
    public const string TableName = "ContractOps_ContractDocument";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("contractDocumentKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ContractDocumentKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("contractKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ContractOps_ContractDocument_contractKey")]
    public Guid ContractKey { get; set; }

    [Column("documentType")]
    [Length(32)]
    public string DocumentType { get; set; } = null!;

    [Column("fileName")]
    [Length(260)]
    public string FileName { get; set; } = null!;

    [Column("contentType")]
    [Length(100)]
    public string ContentType { get; set; } = null!;

    [Column("sizeBytes")]
    public long SizeBytes { get; set; }

    [Column("storagePath")]
    [Length(400)]
    public string StoragePath { get; set; } = null!;

    [Column("uploadedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? UploadedByStaffKey { get; set; }

    [Column("uploadedAtUtc")]
    public DateTime UploadedAtUtc { get; set; }
}
