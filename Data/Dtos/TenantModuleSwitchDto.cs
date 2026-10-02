using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>A row means the module is switched on for the tenant; no row means off.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class TenantModuleSwitchDto
{
    public const string TableName = "Tenancy_ModuleSwitch";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    [Index(IndexTypes.NonClustered)]
    public Guid TenantId { get; set; }

    [Column("moduleKey")]
    [Length(64)]
    public string ModuleKey { get; set; } = null!;

    [Column("switchedOnAtUtc")]
    public DateTime SwitchedOnAtUtc { get; set; }

    [Column("switchedOnByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? SwitchedOnByMemberId { get; set; }
}
