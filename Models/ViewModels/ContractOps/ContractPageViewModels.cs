using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ViewModels.ContractOps;

/// <summary>
/// The Contract Ops pages. Each used to take one model plus several untyped
/// ViewData entries (the contract, its documents, its invoices, the customer
/// list), which the Razor check could not see.
/// </summary>
public sealed record ContractsIndexPageViewModel(
    IReadOnlyList<ContractSummaryRowViewModel> Rows,
    IReadOnlyList<Customer> Customers);

public sealed record ContractDetailPageViewModel(
    CommercialPositionViewModel Position,
    Contract Contract,
    IReadOnlyList<ContractDocument> Documents,
    IReadOnlyList<Invoice> Invoices);

public sealed record InvoiceDocumentPageViewModel(
    Invoice Invoice,
    Contract Contract,
    string CustomerName,
    IReadOnlyList<InvoiceLine> Lines);
