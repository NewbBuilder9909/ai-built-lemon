using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Append-only: a rate change inserts a new row and closes the previous row's
/// effectiveToUtc rather than updating a row in place, so history is audited
/// for free. Nothing outside Services/Staff/IStaffRateRepository and the
/// admin controller should reference this Dto.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class StaffRateDto
{
    public const string TableName = "StaffOps_StaffRate";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_StaffOps_StaffRate_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("costPerHour")]
    public decimal CostPerHour { get; set; }

    [Column("rateCurrency")]
    [Length(3)]
    public string RateCurrency { get; set; } = null!;

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
