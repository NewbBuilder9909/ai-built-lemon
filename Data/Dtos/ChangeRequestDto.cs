using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ChangeRequestDto
{
    public const string TableName = "ProgrammeOps_ChangeRequest";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("changeRequestKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ChangeRequestKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("projectKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_ChangeRequest_projectKey")]
    public Guid ProjectKey { get; set; }

    [Column("title")]
    [Length(256)]
    public string Title { get; set; } = null!;

    [Column("description")]
    [Length(1024)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Description { get; set; }

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("requestedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RequestedByStaffKey { get; set; }

    [Column("decidedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? DecidedByStaffKey { get; set; }

    [Column("decidedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DecidedAtUtc { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
