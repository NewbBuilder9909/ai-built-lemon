using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class InvoiceDto
{
    public const string TableName = "ContractOps_Invoice";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("invoiceKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid InvoiceKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("contractKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ContractOps_Invoice_contractKey")]
    public Guid ContractKey { get; set; }

    [Column("invoiceNumber")]
    [Length(64)]
    public string InvoiceNumber { get; set; } = null!;

    [Column("periodStart")]
    public DateTime PeriodStart { get; set; }

    [Column("periodEnd")]
    public DateTime PeriodEnd { get; set; }

    [Column("currency")]
    [Length(8)]
    public string Currency { get; set; } = null!;

    [Column("subtotal")]
    public decimal Subtotal { get; set; }

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("generatedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? GeneratedByStaffKey { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    // Added by AddInvoiceDraftStage (ContractOps step 06).
    [Column("issuedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? IssuedAtUtc { get; set; }

    [Column("needsReviewHours")]
    public decimal NeedsReviewHours { get; set; }
}
