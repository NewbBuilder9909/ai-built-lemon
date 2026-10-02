namespace ProgrammePulse.Models.ContractOps;

/// <summary>
/// An invoice against a Contract. It is generated as a <see cref="InvoiceStatus.Draft"/>,
/// reviewed, then issued (decision 4 of docs/delivery-evidence-and-contract-assurance.md).
///
/// InvoiceNumber is sequential per contract ("{Reference}-001", ...) and is
/// assigned at <b>issue</b>, never at generation, so a discarded draft leaves
/// no gap and a number is never reused. A draft carries a placeholder
/// (<see cref="DraftNumberPrefix"/>) that is never shown to a customer.
/// </summary>
public sealed record Invoice
{
    public const string DraftNumberPrefix = "DRAFT-";

    public required Guid InvoiceKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ContractKey { get; init; }

    public required string InvoiceNumber { get; init; }

    public required DateOnly PeriodStart { get; init; }

    public required DateOnly PeriodEnd { get; init; }

    public required string Currency { get; init; }

    public required decimal Subtotal { get; init; }

    public required InvoiceStatus Status { get; init; }

    public Guid? GeneratedByStaffKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>When the invoice was issued: its invoice date. Null while a draft.</summary>
    public DateTime? IssuedAtUtc { get; init; }

    /// <summary>
    /// Hours in the period whose source gave no billability (Tempo today).
    /// Never billed and never silently dropped: the draft lists them, and
    /// issuing requires the reviewer to confirm they have been considered.
    /// </summary>
    public decimal NeedsReviewHours { get; init; }

    public bool HasNumber => Status is InvoiceStatus.Issued or InvoiceStatus.Paid;

    public static string DraftNumber(Guid invoiceKey) => DraftNumberPrefix + invoiceKey.ToString("N")[..8].ToUpperInvariant();
}
