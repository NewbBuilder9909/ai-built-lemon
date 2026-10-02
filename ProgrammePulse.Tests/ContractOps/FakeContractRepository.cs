using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ContractOps;

namespace ProgrammePulse.Tests.ContractOps;

/// <summary>
/// In-memory stand-in for IContractRepository, following the hand-rolled
/// fake convention used elsewhere in this test project (see
/// FakeProgrammeRepository) rather than a mocking library. Every Get*Async
/// method filters by tenantId and every Create*/Save*/Add* method stamps
/// TenantId = tenantId on the record before storing.
/// </summary>
public sealed class FakeContractRepository : IContractRepository
{
    public readonly List<Contract> Contracts = [];
    public readonly List<ContractDocument> Documents = [];
    public readonly List<NonLabourCost> NonLabourCosts = [];
    public readonly List<Invoice> Invoices = [];
    public readonly List<InvoiceLine> InvoiceLines = [];

    public Task<IReadOnlyList<Contract>> GetContractsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<Contract>>(Contracts.Where(c => c.TenantId == tenantId).ToList());

    public Task<Contract?> GetContractByKeyAsync(Guid contractKey, Guid tenantId) =>
        Task.FromResult(Contracts.FirstOrDefault(c => c.ContractKey == contractKey && c.TenantId == tenantId));

    public Task<Contract> CreateContractAsync(Contract contract, Guid tenantId)
    {
        contract = contract with { TenantId = tenantId };
        Contracts.Add(contract);
        return Task.FromResult(contract);
    }

    public Task<Contract> UpdateStatusAsync(Guid contractKey, ContractStatus status, string? notes, Guid tenantId)
    {
        var existing = Contracts.First(c => c.ContractKey == contractKey && c.TenantId == tenantId);
        Contracts.Remove(existing);
        var updated = existing with { Status = status, Notes = notes };
        Contracts.Add(updated);
        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<ContractDocument>> GetDocumentsAsync(Guid contractKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<ContractDocument>>(Documents.Where(d => d.ContractKey == contractKey && d.TenantId == tenantId).ToList());

    public Task<ContractDocument?> GetDocumentAsync(Guid contractKey, Guid contractDocumentKey, Guid tenantId) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.ContractKey == contractKey && d.ContractDocumentKey == contractDocumentKey && d.TenantId == tenantId));

    public Task<ContractDocument> SaveDocumentAsync(ContractDocument document, Guid tenantId)
    {
        document = document with { TenantId = tenantId };
        Documents.Add(document);
        return Task.FromResult(document);
    }

    public Task<IReadOnlyList<NonLabourCost>> GetNonLabourCostsAsync(Guid contractKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<NonLabourCost>>(NonLabourCosts.Where(c => c.ContractKey == contractKey && c.TenantId == tenantId).ToList());

    public Task<NonLabourCost> AddNonLabourCostAsync(NonLabourCost cost, Guid tenantId)
    {
        cost = cost with { TenantId = tenantId };
        NonLabourCosts.Add(cost);
        return Task.FromResult(cost);
    }

    public Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid contractKey, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<Invoice>>(Invoices.Where(i => i.ContractKey == contractKey && i.TenantId == tenantId).ToList());

    public Task<(Invoice Invoice, IReadOnlyList<InvoiceLine> Lines)?> GetInvoiceAsync(Guid contractKey, Guid invoiceKey, Guid tenantId)
    {
        var invoice = Invoices.FirstOrDefault(i => i.ContractKey == contractKey && i.InvoiceKey == invoiceKey && i.TenantId == tenantId);
        if (invoice is null)
        {
            return Task.FromResult<(Invoice, IReadOnlyList<InvoiceLine>)?>(null);
        }

        IReadOnlyList<InvoiceLine> lines = InvoiceLines.Where(l => l.InvoiceKey == invoiceKey).ToList();
        return Task.FromResult<(Invoice, IReadOnlyList<InvoiceLine>)?>((invoice, lines));
    }

    public Task<Invoice> CreateInvoiceAsync(Invoice invoice, IReadOnlyList<InvoiceLine> lines, Guid tenantId)
    {
        invoice = invoice with { TenantId = tenantId };
        Invoices.Add(invoice);
        InvoiceLines.AddRange(lines.Select(l => l with { TenantId = tenantId }));
        return Task.FromResult(invoice);
    }

    public Task<string?> IssueInvoiceAsync(Guid contractKey, Guid invoiceKey, string contractReference, DateTime issuedAtUtc, Guid tenantId)
    {
        var index = Invoices.FindIndex(i => i.InvoiceKey == invoiceKey && i.ContractKey == contractKey && i.TenantId == tenantId && i.Status == InvoiceStatus.Draft);
        if (index < 0)
        {
            return Task.FromResult<string?>(null);
        }

        var numbered = Invoices.Count(i => i.ContractKey == contractKey && i.TenantId == tenantId && i.HasNumber);
        var number = $"{contractReference}-{numbered + 1:D3}";
        Invoices[index] = Invoices[index] with { Status = InvoiceStatus.Issued, InvoiceNumber = number, IssuedAtUtc = issuedAtUtc };
        return Task.FromResult<string?>(number);
    }

    public Task<bool> MoveInvoiceAsync(Guid invoiceKey, InvoiceStatus from, InvoiceStatus to, Guid tenantId)
    {
        var index = Invoices.FindIndex(i => i.InvoiceKey == invoiceKey && i.TenantId == tenantId && i.Status == from);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Invoices[index] = Invoices[index] with { Status = to };
        return Task.FromResult(true);
    }
}
