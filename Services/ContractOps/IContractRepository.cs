using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Admin-only, structurally — nothing outside StaffContractController and
/// IContractCommercialService should ever call these methods, same
/// principle as IStaffRateRepository. One repository across the Contract +
/// ContractDocument aggregate, following the ProgrammeRepository precedent.
///
/// Tenant boundary: every method takes an explicit tenantId (never inferred)
/// — see docs/tenancy.md and ProgrammeRepository for the pattern this
/// mirrors. Get*Async methods filter by tenantId; Create/Save/Add methods
/// stamp TenantId on the row they write and validate any foreign key they
/// carry belongs to the same tenant, throwing CrossTenantReferenceException
/// otherwise; by-key mutations filter their target row by tenantId too, so a
/// key guessed from another tenant matches nothing.
/// </summary>
public interface IContractRepository
{
    Task<IReadOnlyList<Contract>> GetContractsAsync(Guid tenantId);

    Task<Contract?> GetContractByKeyAsync(Guid contractKey, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">CustomerKey belongs to a different tenant or doesn't exist.</exception>
    Task<Contract> CreateContractAsync(Contract contract, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">The contract belongs to a different tenant or doesn't exist.</exception>
    Task<Contract> UpdateStatusAsync(Guid contractKey, ContractStatus status, string? notes, Guid tenantId);

    Task<IReadOnlyList<ContractDocument>> GetDocumentsAsync(Guid contractKey, Guid tenantId);

    Task<ContractDocument?> GetDocumentAsync(Guid contractKey, Guid contractDocumentKey, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">ContractKey belongs to a different tenant or doesn't exist.</exception>
    Task<ContractDocument> SaveDocumentAsync(ContractDocument document, Guid tenantId);

    Task<IReadOnlyList<NonLabourCost>> GetNonLabourCostsAsync(Guid contractKey, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">ContractKey belongs to a different tenant or doesn't exist.</exception>
    Task<NonLabourCost> AddNonLabourCostAsync(NonLabourCost cost, Guid tenantId);

    Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid contractKey, Guid tenantId);

    Task<(Invoice Invoice, IReadOnlyList<InvoiceLine> Lines)?> GetInvoiceAsync(Guid contractKey, Guid invoiceKey, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">ContractKey belongs to a different tenant or doesn't exist.</exception>
    Task<Invoice> CreateInvoiceAsync(Invoice invoice, IReadOnlyList<InvoiceLine> lines, Guid tenantId);

    /// <summary>
    /// Issues a draft: assigns the contract's next invoice number
    /// ("{reference}-NNN", counting only issued and paid invoices) and the
    /// issue date, in one locked transaction so two concurrent issues can't
    /// take the same number. Returns the number, or null if the invoice is
    /// not a draft of this contract in this tenant.
    /// </summary>
    Task<string?> IssueInvoiceAsync(Guid contractKey, Guid invoiceKey, string contractReference, DateTime issuedAtUtc, Guid tenantId);

    /// <summary>Moves an invoice from one status to another only if it is still in <paramref name="from"/>. False otherwise.</summary>
    Task<bool> MoveInvoiceAsync(Guid invoiceKey, InvoiceStatus from, InvoiceStatus to, Guid tenantId);
}
