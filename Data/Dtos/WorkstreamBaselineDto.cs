using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class WorkstreamBaselineDto
{
    public const string TableName = "ProgrammeOps_WorkstreamBaseline";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("workstreamKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid WorkstreamKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("baselineHours")]
    public decimal BaselineHours { get; set; }

    [Column("lockedAtUtc")]
    public DateTime LockedAtUtc { get; set; }

    [Column("lockedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? LockedByStaffKey { get; set; }
}
