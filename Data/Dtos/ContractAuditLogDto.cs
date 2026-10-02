using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ContractAuditLogDto
{
    public const string TableName = "ContractOps_AuditLog";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("logKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LogKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("entityType")]
    [Length(64)]
    public string EntityType { get; set; } = null!;

    [Column("entityId")]
    [Length(128)]
    public string EntityId { get; set; } = null!;

    [Column("action")]
    [Length(64)]
    public string Action { get; set; } = null!;

    [Column("actorMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? ActorMemberId { get; set; }

    [Column("detailJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DetailJson { get; set; }

    [Column("timestampUtc")]
    public DateTime TimestampUtc { get; set; }
}
