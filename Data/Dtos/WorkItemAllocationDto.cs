using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class WorkItemAllocationDto
{
    public const string TableName = "ProgrammeOps_WorkItemAllocation";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("allocationKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid AllocationKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("workItemKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_WorkItemAllocation_workItemKey")]
    public Guid WorkItemKey { get; set; }

    [Column("staffKey")]
    public Guid StaffKey { get; set; }

    [Column("isPrimary")]
    public bool IsPrimary { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
