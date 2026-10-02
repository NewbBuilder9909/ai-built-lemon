namespace ProgrammePulse.Models.ContractOps;

/// <summary>
/// Metadata for a stored contract document. The file itself lives outside
/// wwwroot (see IContractDocumentStorageService) — StoragePath is the local
/// path or storage-account URL it was written to, never a web-servable URL.
/// Reads rebuild the location from the keys and only check it against this.
/// </summary>
public sealed record ContractDocument
{
    public required Guid ContractDocumentKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ContractKey { get; init; }

    public required ContractDocumentType DocumentType { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required string StoragePath { get; init; }

    public Guid? UploadedByStaffKey { get; init; }

    public required DateTime UploadedAtUtc { get; init; }
}
