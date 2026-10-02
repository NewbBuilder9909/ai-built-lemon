using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName), ExplicitColumns]
internal sealed class ExecutiveDataEventDto
{
    public const string TableName = "ExecutiveReview_DataEvent";
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("operationKey"), Index(IndexTypes.UniqueNonClustered)] public Guid OperationKey { get; set; }
    [Column("operation")] public string Operation { get; set; } = null!;
    [Column("targetKey"), NullSetting(NullSetting = NullSettings.Null)] public Guid? TargetKey { get; set; }
    [Column("reason")] public string Reason { get; set; } = null!;
    [Column("actorMemberId"), NullSetting(NullSetting = NullSettings.Null)] public int? ActorMemberId { get; set; }
    [Column("countsJson"), SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string CountsJson { get; set; } = null!;
    [Column("recordedAtUtc")] public DateTime RecordedAtUtc { get; set; }
}
