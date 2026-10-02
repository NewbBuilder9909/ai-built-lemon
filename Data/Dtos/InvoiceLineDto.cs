using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class InvoiceLineDto
{
    public const string TableName = "ContractOps_InvoiceLine";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("invoiceLineKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid InvoiceLineKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("invoiceKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ContractOps_InvoiceLine_invoiceKey")]
    public Guid InvoiceKey { get; set; }

    [Column("description")]
    [Length(256)]
    public string Description { get; set; } = null!;

    [Column("quantity")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? Quantity { get; set; }

    [Column("unitRate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? UnitRate { get; set; }

    [Column("lineTotal")]
    public decimal LineTotal { get; set; }
}
