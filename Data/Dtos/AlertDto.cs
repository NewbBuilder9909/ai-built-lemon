using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class AlertDto
{
    public const string TableName = "ProgrammeOps_Alert";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("alertKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid AlertKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("type")]
    [Length(32)]
    public string Type { get; set; } = null!;

    [Column("entityKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_Alert_entityKey")]
    public Guid EntityKey { get; set; }

    [Column("message")]
    [Length(512)]
    public string Message { get; set; } = null!;

    [Column("raisedAtUtc")]
    public DateTime RaisedAtUtc { get; set; }

    [Column("acknowledgedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? AcknowledgedAtUtc { get; set; }

    [Column("acknowledgedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? AcknowledgedByStaffKey { get; set; }
}
