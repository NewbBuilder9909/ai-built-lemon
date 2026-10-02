using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName), ExplicitColumns]
internal sealed class ExecutiveDecisionVersionDto
{
    public const string TableName = "ExecutiveReview_DecisionVersion";
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("decisionKey")] public Guid DecisionKey { get; set; }
    [Column("version")] public int Version { get; set; }
    [Column("status")] public int Status { get; set; }
    [Column("dueOn")] public DateTime DueOn { get; set; }
    [Column("recordedAtUtc")] public DateTime RecordedAtUtc { get; set; }
    [Column("actorMemberId")] public int ActorMemberId { get; set; }
    [Column("payloadJson"), SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string PayloadJson { get; set; } = null!;
}
