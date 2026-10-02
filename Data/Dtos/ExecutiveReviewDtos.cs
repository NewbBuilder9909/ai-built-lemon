using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName), ExplicitColumns]
internal sealed class MarketSettingsVersionDto
{
    public const string TableName = "ExecutiveReview_MarketVersion";
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("version")] public int Version { get; set; }
    [Column("settingsJson"), SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string SettingsJson { get; set; } = null!;
    [Column("effectiveAtUtc")] public DateTime EffectiveAtUtc { get; set; }
    [Column("changedByMemberId")] public int ChangedByMemberId { get; set; }
}

[TableName(TableName), ExplicitColumns]
internal sealed class ExecutivePackDto
{
    public const string TableName = "ExecutiveReview_Pack";
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("packKey"), Index(IndexTypes.UniqueNonClustered)] public Guid PackKey { get; set; }
    [Column("capturedAtUtc")] public DateTime CapturedAtUtc { get; set; }
    [Column("capturedByMemberId")] public int CapturedByMemberId { get; set; }
    [Column("payloadJson"), SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string PayloadJson { get; set; } = null!;
}
