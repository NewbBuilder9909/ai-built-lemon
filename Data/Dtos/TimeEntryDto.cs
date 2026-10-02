using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class TimeEntryDto
{
    public const string TableName = "ProgrammeOps_TimeEntry";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("timeEntryKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid TimeEntryKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("workItemKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_TimeEntry_workItemKey")]
    public Guid? WorkItemKey { get; set; }

    [Column("staffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_TimeEntry_staffKey")]
    public Guid? StaffKey { get; set; }

    [Column("durationHours")]
    public decimal DurationHours { get; set; }

    [Column("startedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? StartedAtUtc { get; set; }

    [Column("workDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? WorkDate { get; set; }

    [Column("isBillable")]
    public bool IsBillable { get; set; } = true;

    [Column("billabilityKnown")]
    public bool BillabilityKnown { get; set; } = true;

    [Column("externalSource")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalSource { get; set; }

    [Column("externalId")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_TimeEntry_externalId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalId { get; set; }

    [Column("sourceWorkItemExternalId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SourceWorkItemExternalId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
