using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ExternalIdentityLinkDto
{
    public const string TableName = "ProgrammeOps_ExternalIdentityLink";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("linkKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LinkKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("connectionKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ConnectionKey { get; set; }

    [Column("externalSource")]
    [Length(64)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_ExternalIdentityLink_externalSource")]
    public string ExternalSource { get; set; } = null!;

    [Column("externalUserId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalUserId { get; set; }

    [Column("email")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Email { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_ExternalIdentityLink_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("createdByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? CreatedByMemberId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
