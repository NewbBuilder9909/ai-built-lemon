using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class NonLabourCostDto
{
    public const string TableName = "ContractOps_NonLabourCost";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("nonLabourCostKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid NonLabourCostKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("contractKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ContractOps_NonLabourCost_contractKey")]
    public Guid ContractKey { get; set; }

    [Column("description")]
    [Length(256)]
    public string Description { get; set; } = null!;

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("currency")]
    [Length(8)]
    public string Currency { get; set; } = null!;

    [Column("incurredOn")]
    public DateTime IncurredOn { get; set; }

    [Column("recordedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RecordedByStaffKey { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
