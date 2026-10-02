namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Raised by IContractDocumentStorageService when an upload fails validation
/// (wrong extension, too large, or fails the magic-byte signature check).
/// The controller catches this and surfaces the message rather than letting
/// it bubble as a 500.
/// </summary>
public sealed class ContractDocumentValidationException(string message) : Exception(message);
