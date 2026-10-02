using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class ContractDto
{
    public const string TableName = "ContractOps_Contract";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("contractKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ContractKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("customerKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ContractOps_Contract_customerKey")]
    public Guid CustomerKey { get; set; }

    [Column("reference")]
    [Length(256)]
    public string Reference { get; set; } = null!;

    [Column("commercialModel")]
    [Length(32)]
    public string CommercialModel { get; set; } = null!;

    [Column("totalContractValue")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? TotalContractValue { get; set; }

    [Column("annualValue")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? AnnualValue { get; set; }

    [Column("billRate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? BillRate { get; set; }

    [Column("currency")]
    [Length(8)]
    public string Currency { get; set; } = null!;

    [Column("startDate")]
    public DateTime StartDate { get; set; }

    [Column("endDate")]
    public DateTime EndDate { get; set; }

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("notes")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Notes { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
