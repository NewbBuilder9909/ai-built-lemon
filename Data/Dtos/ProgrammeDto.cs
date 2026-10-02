using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ProgrammeDto
{
    public const string TableName = "ProgrammeOps_Programme";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("programmeKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ProgrammeKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("name")]
    [Length(256)]
    public string Name { get; set; } = null!;

    [Column("description")]
    [Length(1024)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Description { get; set; }

    [Column("customerKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? CustomerKey { get; set; }

    [Column("budgetAmount")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? BudgetAmount { get; set; }

    [Column("budgetCurrency")]
    [Length(8)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? BudgetCurrency { get; set; }

    [Column("externalSource")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalSource { get; set; }

    [Column("externalId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
