namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Raised by IContractDocumentStorageService.OpenReadAsync when a document's
/// file is missing, or its stored path names anything other than the file
/// this service wrote for it. ContractAdminService turns it into "not found".
/// </summary>
public sealed class ContractDocumentUnavailableException(Guid documentKey)
    : Exception($"Contract document {documentKey} is not available from this deployment's storage.");
