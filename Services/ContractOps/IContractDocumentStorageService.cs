using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

public interface IContractDocumentStorageService
{
    /// <summary>
    /// Validates extension/size/signature, writes the file outside wwwroot
    /// under a GUID-derived filename, and persists the document metadata
    /// row. Throws ContractDocumentValidationException on any validation
    /// failure, or Shared.CrossTenantReferenceException if contractKey
    /// doesn't belong to tenantId.
    /// </summary>
    Task<ContractDocument> StoreAsync(Guid contractKey, IFormFile file, ContractDocumentType documentType, Guid? uploadedByStaffKey, Guid tenantId);

    Task<Stream> OpenReadAsync(ContractDocument document);

    Stream OpenRead(ContractDocument document);
}
