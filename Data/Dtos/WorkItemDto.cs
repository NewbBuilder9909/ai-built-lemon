using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class WorkItemDto
{
    public const string TableName = "ProgrammeOps_WorkItem";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("workItemKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid WorkItemKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("workstreamKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_WorkItem_workstreamKey")]
    public Guid WorkstreamKey { get; set; }

    [Column("title")]
    [Length(512)]
    public string Title { get; set; } = null!;

    [Column("stage")]
    [Length(32)]
    public string Stage { get; set; } = null!;

    [Column("rawStatus")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RawStatus { get; set; }

    [Column("isMilestone")]
    public bool IsMilestone { get; set; }

    [Column("assignedStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? AssignedStaffKey { get; set; }

    [Column("dueDateUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DueDateUtc { get; set; }

    [Column("estimatedHours")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? EstimatedHours { get; set; }

    [Column("externalSource")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalSource { get; set; }

    [Column("externalId")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_WorkItem_externalId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalId { get; set; }

    [Column("parentExternalId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ParentExternalId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
