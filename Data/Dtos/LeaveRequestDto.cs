using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class LeaveRequestDto
{
    public const string TableName = "StaffOps_LeaveRequest";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("requestKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid RequestKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_StaffOps_LeaveRequest_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("requestedFrom")]
    public DateTime RequestedFrom { get; set; }

    [Column("requestedTo")]
    public DateTime RequestedTo { get; set; }

    [Column("type")]
    [Length(32)]
    public string Type { get; set; } = null!;

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("approvedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ApprovedByStaffKey { get; set; }

    [Column("notes")]
    [Length(1024)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Notes { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("decidedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DecidedAtUtc { get; set; }
}
