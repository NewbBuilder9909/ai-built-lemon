using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ReportingSnapshotDto
{
    public const string TableName = "ProgrammeOps_ReportingSnapshot";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("snapshotKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid SnapshotKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("capturedAtUtc")]
    public DateTime CapturedAtUtc { get; set; }

    [Column("periodStart")]
    public DateTime PeriodStart { get; set; }

    [Column("periodEnd")]
    public DateTime PeriodEnd { get; set; }

    [Column("totalEstimatedHours")]
    public decimal TotalEstimatedHours { get; set; }

    [Column("totalActualHours")]
    public decimal TotalActualHours { get; set; }

    [Column("totalVarianceHours")]
    public decimal TotalVarianceHours { get; set; }

    [Column("openWorkItems")]
    public int OpenWorkItems { get; set; }

    [Column("blockedWorkItems")]
    public int BlockedWorkItems { get; set; }

    [Column("averageUtilisationPercent")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? AverageUtilisationPercent { get; set; }

    [Column("capturedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? CapturedByStaffKey { get; set; }
}
