namespace ProgrammePulse.Models.ContractOps;

/// <summary>
/// An invoice's stage. Stored by name, so values are appended, never
/// reordered. The only moves are Draft → Issued, Draft → Discarded and
/// Issued → Paid (<see cref="InvoiceLifecycle"/>): an invoice is reviewed
/// before a customer sees it, and nothing goes back.
/// </summary>
public enum InvoiceStatus
{
    Issued,
    Paid,

    /// <summary>Generated, not yet reviewed. Has no invoice number and is never sent.</summary>
    Draft,

    /// <summary>A draft rejected at review. Kept on record; it never had a number.</summary>
    Discarded,
}

/// <summary>The allowed status moves, in one place so the service and its tests agree.</summary>
public static class InvoiceLifecycle
{
    public static bool CanMove(InvoiceStatus from, InvoiceStatus to) => (from, to) switch
    {
        (InvoiceStatus.Draft, InvoiceStatus.Issued) => true,
        (InvoiceStatus.Draft, InvoiceStatus.Discarded) => true,
        (InvoiceStatus.Issued, InvoiceStatus.Paid) => true,
        _ => false,
    };
}
