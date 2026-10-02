namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Raised by IInvoiceGenerationService when a period has nothing to invoice
/// (no Time &amp; Materials billable hours, no non-labour costs incurred).
/// The controller catches this and surfaces the message rather than
/// creating an empty invoice.
/// </summary>
public sealed class InvoiceGenerationException(string message) : Exception(message);
