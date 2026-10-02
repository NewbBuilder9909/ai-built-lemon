using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ProgrammeStakeholderDto
{
    public const string TableName = "ProgrammeOps_ProgrammeStakeholder";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("programmeStakeholderKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ProgrammeStakeholderKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("programmeKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_ProgrammeStakeholder_programmeKey")]
    public Guid ProgrammeKey { get; set; }

    [Column("staffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? StaffKey { get; set; }

    [Column("externalName")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalName { get; set; }

    [Column("role")]
    [Length(32)]
    public string Role { get; set; } = null!;

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
