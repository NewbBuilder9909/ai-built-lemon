using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class AvailabilityDto
{
    public const string TableName = "StaffOps_Availability";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_StaffOps_Availability_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("startTime")]
    public TimeSpan StartTime { get; set; }

    [Column("endTime")]
    public TimeSpan EndTime { get; set; }

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("source")]
    [Length(32)]
    public string Source { get; set; } = null!;
}
