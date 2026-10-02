using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class EstimateBaselineDto
{
    public const string TableName = "ProgrammeOps_EstimateBaseline";
    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }
    [Column("estimateBaselineKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid EstimateBaselineKey { get; set; }
    [Column("tenantId")]
    [Index(IndexTypes.NonClustered, Name = "IX_EstimateBaseline_tenantId")]
    public Guid TenantId { get; set; }
    [Column("workItemKey")]
    public Guid WorkItemKey { get; set; }
    [Column("estimatorStaffKey")]
    public Guid EstimatorStaffKey { get; set; }
    [Column("originalEffortHours")]
    public decimal OriginalEffortHours { get; set; }
    [Column("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }
    [Column("reviewedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReviewedAtUtc { get; set; }
    [Column("reviewedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ReviewedByStaffKey { get; set; }
    [Column("isComparable")]
    public bool IsComparable { get; set; }
    [Column("reviewNote")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ReviewNote { get; set; }
}
