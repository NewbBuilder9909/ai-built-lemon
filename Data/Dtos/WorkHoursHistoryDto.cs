using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Append-only, identical shape to StaffRateDto — see WorkHoursHistory's doc
/// comment for why. Nothing outside Services/Staff/IWorkHoursHistoryRepository
/// and the admin controller should reference this Dto.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class WorkHoursHistoryDto
{
    public const string TableName = "StaffOps_WorkHoursHistory";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_StaffOps_WorkHoursHistory_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("hoursPerWeek")]
    public decimal HoursPerWeek { get; set; }

    [Column("effectiveFromUtc")]
    public DateTime EffectiveFromUtc { get; set; }

    [Column("effectiveToUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? EffectiveToUtc { get; set; }

    [Column("changedByStaffKey")]
    public Guid ChangedByStaffKey { get; set; }

    [Column("changedAtUtc")]
    public DateTime ChangedAtUtc { get; set; }
}
