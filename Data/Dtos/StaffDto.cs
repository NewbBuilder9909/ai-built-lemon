using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class StaffDto
{
    public const string TableName = "StaffOps_Staff";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid StaffKey { get; set; }

    [Column("memberId")]
    [Index(IndexTypes.UniqueNonClustered)]
    public int MemberId { get; set; }

    [Column("fullName")]
    [Length(256)]
    public string FullName { get; set; } = null!;

    [Column("email")]
    [Length(256)]
    [Index(IndexTypes.UniqueNonClustered)]
    public string Email { get; set; } = null!;

    [Column("jobTitle")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? JobTitle { get; set; }

    [Column("department")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Department { get; set; }

    [Column("team")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Team { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }

    [Column("defaultWorkHoursPerWeek")]
    public decimal DefaultWorkHoursPerWeek { get; set; }

    [Column("calendarProvider")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? CalendarProvider { get; set; }

    [Column("calendarUserId")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? CalendarUserId { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
