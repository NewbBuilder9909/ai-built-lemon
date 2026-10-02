using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class PlannedAllocationDto
{
    public const string TableName = "ProgrammeOps_PlannedAllocation";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("plannedAllocationKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid PlannedAllocationKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("projectKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_PlannedAllocation_projectKey")]
    public Guid ProjectKey { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_PlannedAllocation_staffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? StaffKey { get; set; }

    [Column("title")]
    [Length(512)]
    public string Title { get; set; } = null!;

    [Column("startUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? StartUtc { get; set; }

    [Column("endUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? EndUtc { get; set; }

    [Column("allocatedHours")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? AllocatedHours { get; set; }

    [Column("allocationPercent")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? AllocationPercent { get; set; }

    [Column("rawType")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RawType { get; set; }

    [Column("externalSource")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalSource { get; set; }

    [Column("externalId")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_PlannedAllocation_externalId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalId { get; set; }

    [Column("externalResourceId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalResourceId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
