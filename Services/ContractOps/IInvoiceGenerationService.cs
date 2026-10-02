using ProgrammePulse.Models.ContractOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>Admin-only, same gating as IContractCommercialService.</summary>
public interface IInvoiceGenerationService
{
    /// <exception cref="InvoiceGenerationException">The period has nothing to invoice.</exception>
    Task<Invoice> GenerateAsync(Guid contractKey, DateOnly periodStart, DateOnly periodEnd, Guid? generatedByStaffKey, Guid tenantId);
}
