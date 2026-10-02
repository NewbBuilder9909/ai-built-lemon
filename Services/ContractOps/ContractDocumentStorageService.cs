using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Azure Files-backed storage for contract documents, with Azure Blob and local
/// file fallbacks for tests and local development. File-share semantics preserve
/// the application’s shared file access model while still allowing the service to
/// run in cloud-hosted environments without a server-local disk dependency.
/// </summary>
public sealed class ContractDocumentStorageService(
    IWebHostEnvironment webHostEnvironment,
    IContractRepository contractRepository,
    TimeProvider timeProvider) : IContractDocumentStorageService
{
    private const string RelativeStorageDirectory = "App_Data/contracts";
    private const long MaxSizeBytes = 25 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, (string ContentType, byte[] Signature)> Rules =
        new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", "%PDF-"u8.ToArray()),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [0x50, 0x4B, 0x03, 0x04]),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [0x50, 0x4B, 0x03, 0x04]),
            [".doc"] = ("application/msword", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            [".xls"] = ("application/vnd.ms-excel", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])
        };

    public async Task<ContractDocument> StoreAsync(Guid contractKey, IFormFile file, ContractDocumentType documentType, Guid? uploadedByStaffKey, Guid tenantId)
    {
        if (file.Length == 0)
        {
            throw new ContractDocumentValidationException("The uploaded file is empty.");
        }

        if (file.Length > MaxSizeBytes)
        {
            throw new ContractDocumentValidationException($"File exceeds the {MaxSizeBytes / 1024 / 1024}MB limit.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!Rules.TryGetValue(extension, out var rule))
        {
            throw new ContractDocumentValidationException(
                $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", Rules.Keys)}.");
        }

        if (await contractRepository.GetContractByKeyAsync(contractKey, tenantId) is null)
        {
            throw new CrossTenantReferenceException("Contract", contractKey);
        }

        var signature = new byte[rule.Signature.Length];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAsync(signature);
            if (read < rule.Signature.Length || !signature.SequenceEqual(rule.Signature))
            {
                throw new ContractDocumentValidationException(
                    $"The uploaded file's content doesn't match a {extension} file — its extension may have been spoofed.");
            }
        }

        var documentKey = Guid.NewGuid();
        var storedFileName = $"{documentKey:N}{extension}";
        var relativeBlobPath = $"{RelativeStorageDirectory}/{contractKey:N}/{storedFileName}";
        var relativeSharePath = $"{contractKey:N}/{storedFileName}";
        var absolutePath = UnderStorageRoot(Path.Combine(webHostEnvironment.ContentRootPath, RelativeStorageDirectory, contractKey.ToString("N"), storedFileName));

        try
        {
            var storagePath = await UploadAsync(relativeSharePath, relativeBlobPath, file);

            var document = new ContractDocument
            {
                ContractDocumentKey = documentKey,
                ContractKey = contractKey,
                DocumentType = documentType,
                FileName = file.FileName,
                ContentType = rule.ContentType,
                SizeBytes = file.Length,
                StoragePath = storagePath,
                UploadedByStaffKey = uploadedByStaffKey,
                UploadedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

            return await contractRepository.SaveDocumentAsync(document, tenantId);
        }
        catch
        {
            if (TryResolveFileShareClient(out var shareClient))
            {
                var fileClient = GetShareFileClient(shareClient, relativeSharePath);
                await fileClient.DeleteIfExistsAsync();
            }
            else if (TryResolveBlobClient(out var containerClient))
            {
                var blobClient = containerClient.GetBlobClient(relativeBlobPath);
                await blobClient.DeleteIfExistsAsync();
            }
            else if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            throw;
        }
    }

    /// <summary>
    /// Opens the file this service wrote for <paramref name="document"/>, and
    /// only that file. The location is rebuilt from the document's own keys
    /// (contract, document, extension) under this deployment's storage root
    /// or storage account; the stored path must name that same file or the
    /// read is refused. Opening StoragePath verbatim meant any row whose path
    /// was altered or restored from elsewhere could read an arbitrary local
    /// file or URL (Aikido: path traversal). A refused or missing file throws
    /// <see cref="ContractDocumentUnavailableException"/>.
    /// </summary>
    public async Task<Stream> OpenReadAsync(ContractDocument document)
    {
        var extension = Path.GetExtension(document.StoragePath);
        if (!Rules.ContainsKey(extension))
        {
            throw new ContractDocumentUnavailableException(document.ContractDocumentKey);
        }

        var storedFileName = $"{document.ContractDocumentKey:N}{extension}";
        var relativeSharePath = $"{document.ContractKey:N}/{storedFileName}";
        var relativeBlobPath = $"{RelativeStorageDirectory}/{relativeSharePath}";

        if (Uri.TryCreate(document.StoragePath, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            // The configured, authenticated client, never one built from the
            // stored URL: that would let a row choose the host.
            if (TryResolveFileShareClient(out var shareClient))
            {
                var fileClient = shareClient.GetRootDirectoryClient()
                    .GetSubdirectoryClient(document.ContractKey.ToString("N"))
                    .GetFileClient(storedFileName);
                if (SameResource(fileClient.Uri, uri))
                {
                    return await fileClient.OpenReadAsync();
                }
            }

            if (TryResolveBlobClient(out var containerClient))
            {
                var blobClient = containerClient.GetBlobClient(relativeBlobPath);
                if (SameResource(blobClient.Uri, uri))
                {
                    return await blobClient.OpenReadAsync();
                }
            }

            throw new ContractDocumentUnavailableException(document.ContractDocumentKey);
        }

        // A local path: the stored one must end in this document's own
        // contract folder and file name. The file is then opened under this
        // deployment's root, so a moved content root still resolves, including
        // a Windows path read on Linux (neither name can contain a backslash).
        var storedPath = document.StoragePath.Replace('\\', '/');
        var storedName = Path.GetFileName(storedPath);
        var storedFolder = Path.GetFileName(Path.GetDirectoryName(storedPath));
        if (!string.Equals(storedName, storedFileName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(storedFolder, document.ContractKey.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractDocumentUnavailableException(document.ContractDocumentKey);
        }

        var absolutePath = Path.Combine(webHostEnvironment.ContentRootPath, RelativeStorageDirectory, document.ContractKey.ToString("N"), storedFileName);
        if (!File.Exists(absolutePath))
        {
            throw new ContractDocumentUnavailableException(document.ContractDocumentKey);
        }

        // A stored path is data, not a command: it must still resolve inside the store.
        return new FileStream(
            UnderStorageRoot(absolutePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>Same scheme, host, port and path; any query (a SAS) is ignored.</summary>
    private static bool SameResource(Uri expected, Uri stored) =>
        Uri.Compare(expected, stored, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;

    public Stream OpenRead(ContractDocument document) => OpenReadAsync(document).GetAwaiter().GetResult();

    /// <summary>
    /// The full path, if it resolves inside the local document store; otherwise refused.
    /// File names are server-generated and extensions allow-listed, so nothing should
    /// reach this today; the check keeps it that way (Aikido: path traversal).
    /// </summary>
    private string UnderStorageRoot(string path)
    {
        var root = Path.GetFullPath(Path.Combine(webHostEnvironment.ContentRootPath, RelativeStorageDirectory))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new ContractDocumentValidationException("The document's storage location is outside the document store.");
    }

    private async Task<string> UploadAsync(string relativeSharePath, string relativeBlobPath, IFormFile file)
    {
        if (TryResolveFileShareClient(out var shareClient))
        {
            var fileClient = GetShareFileClient(shareClient, relativeSharePath);
            await fileClient.DeleteIfExistsAsync();
            await using var source = file.OpenReadStream();
            await fileClient.UploadAsync(source);
            return fileClient.Uri.AbsoluteUri;
        }

        if (TryResolveBlobClient(out var containerClient))
        {
            var blobClient = containerClient.GetBlobClient(relativeBlobPath);
            await using var source = file.OpenReadStream();
            await blobClient.UploadAsync(source, overwrite: true);
            return blobClient.Uri.AbsoluteUri;
        }

        var absolutePath = UnderStorageRoot(Path.Combine(webHostEnvironment.ContentRootPath, RelativeStorageDirectory, relativeSharePath.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await using (var source = file.OpenReadStream())
        await using (var destination = File.Create(absolutePath))
        {
            await source.CopyToAsync(destination);
        }

        return absolutePath;
    }

    internal static bool TryResolveFileShareSettings(out string? connectionString, out string? shareName)
    {
        connectionString = ResolveSetting(
            "AzureFiles:ConnectionString",
            "AzureFiles__ConnectionString",
            "AzureStorage:FileShareConnectionString",
            "AzureStorage__FileShareConnectionString",
            "FileShare:ConnectionString",
            "FileShare__ConnectionString",
            "Contract:FileShareConnectionString",
            "Contract__FileShareConnectionString");

        shareName = ResolveSetting(
            "AzureFiles:ShareName",
            "AzureFiles__ShareName",
            "AzureStorage:FileShareName",
            "AzureStorage__FileShareName",
            "FileShare:ShareName",
            "FileShare__ShareName",
            "Contract:FileShareName",
            "Contract__FileShareName");

        return !string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(shareName);
    }

    private static bool TryResolveFileShareClient(out ShareClient shareClient)
    {
        if (!TryResolveFileShareSettings(out var connectionString, out var shareName))
        {
            shareClient = null!;
            return false;
        }

        shareClient = new ShareClient(connectionString, shareName);
        return true;
    }

    private static ShareFileClient GetShareFileClient(ShareClient shareClient, string relativeSharePath)
    {
        shareClient.CreateIfNotExists();

        var segments = relativeSharePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rootDirectoryClient = shareClient.GetRootDirectoryClient();
        ShareDirectoryClient directoryClient = rootDirectoryClient;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            directoryClient = directoryClient.GetSubdirectoryClient(segments[index]);
            directoryClient.CreateIfNotExists();
        }

        var fileName = segments[^1];
        return directoryClient.GetFileClient(fileName);
    }

    private static bool TryResolveBlobClient(out BlobContainerClient containerClient)
    {
        var connectionString = GetSetting("AzureStorage:ConnectionString", "AzureStorage__ConnectionString", "BlobStorage:ConnectionString", "BlobStorage__ConnectionString", "Contract:BlobConnectionString", "Contract__BlobConnectionString");
        var containerName = GetSetting("AzureStorage:ContainerName", "AzureStorage__ContainerName", "BlobStorage:ContainerName", "BlobStorage__ContainerName", "Contract:BlobContainerName", "Contract__BlobContainerName");

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
        {
            containerClient = null!;
            return false;
        }

        containerClient = new BlobContainerClient(connectionString, containerName);
        return true;
    }

    private static string? ResolveSetting(params string[] keys)
    {
        foreach (var key in keys)
        {
            var alternatives = new[]
            {
                key,
                key.Replace(':', '_'),
                key.Replace(':', '_').Replace("__", "_"),
                key.Replace(':', '_').Replace("__", "_").ToUpperInvariant(),
                key.Replace(':', '_').Replace("__", "_").Replace("-", "_").ToUpperInvariant(),
                key.Replace(':', '_').Replace("__", "_").Replace("-", "_")
            };

            foreach (var alternative in alternatives.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var value = Environment.GetEnvironmentVariable(alternative);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? GetSetting(params string[] keys) => ResolveSetting(keys);
}
