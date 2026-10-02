using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ContractOps;

public class ContractDocumentStorageServiceTests
{
    [Fact]
    public async Task Foreign_contract_is_rejected_before_file_write()
    {
        var root = NewRoot();
        try
        {
            var repository = new ContractRepositoryStub();
            var service = new ContractDocumentStorageService(new TestEnvironment(root), repository, TimeProvider.System);

            await Assert.ThrowsAsync<CrossTenantReferenceException>(() =>
                service.StoreAsync(Guid.NewGuid(), PdfFile(), ContractDocumentType.SOW, null, Guid.NewGuid()));

            Assert.False(Directory.Exists(Path.Combine(root, "App_Data", "contracts")));
            Assert.False(repository.SaveCalled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_repository_save_removes_new_file()
    {
        var root = NewRoot();
        try
        {
            var repository = new ContractRepositoryStub { Contract = Contract() };
            var service = new ContractDocumentStorageService(new TestEnvironment(root), repository, TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StoreAsync(repository.Contract.ContractKey, PdfFile(), ContractDocumentType.SOW, null, Guid.NewGuid()));

            Assert.True(repository.SaveCalled);
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Local_document_stream_can_be_opened_asynchronously()
    {
        var root = NewRoot();
        try
        {
            var repository = new ContractRepositoryStub { Contract = Contract(), ThrowOnSave = false };
            var service = new ContractDocumentStorageService(new TestEnvironment(root), repository, TimeProvider.System);

            var document = await service.StoreAsync(repository.Contract.ContractKey, PdfFile(), ContractDocumentType.SOW, null, Guid.NewGuid());

            await using var stream = await service.OpenReadAsync(document);
            using var reader = new StreamReader(stream, leaveOpen: true);
            var content = await reader.ReadToEndAsync();

            Assert.Contains("%PDF", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_stored_path_is_opened_only_where_this_service_would_have_written_it()
    {
        var root = NewRoot();
        var outside = Path.Combine(Path.GetTempPath(), "contract-storage-outside-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllTextAsync(outside, "%PDF-1.4 not this document");
        try
        {
            var repository = new ContractRepositoryStub { Contract = Contract(), ThrowOnSave = false };
            var service = new ContractDocumentStorageService(new TestEnvironment(root), repository, TimeProvider.System);
            var stored = await service.StoreAsync(repository.Contract.ContractKey, PdfFile(), ContractDocumentType.SOW, null, Guid.NewGuid());
            var sibling = await service.StoreAsync(repository.Contract.ContractKey, PdfFile(), ContractDocumentType.SOW, null, Guid.NewGuid());

            // A row whose path was altered, restored from elsewhere or copied
            // from another document must not become a read of that file.
            string[] tampered =
            [
                outside,
                Path.Combine(root, "App_Data", "contracts", stored.ContractKey.ToString("N"), "..", "..", "..", Path.GetFileName(outside)),
                sibling.StoragePath,
                "https://attacker.example/" + Path.GetFileName(stored.StoragePath),
                Path.ChangeExtension(stored.StoragePath, ".txt"),
            ];
            foreach (var path in tampered)
            {
                await Assert.ThrowsAsync<ContractDocumentUnavailableException>(() => service.OpenReadAsync(stored with { StoragePath = path }));
            }

            // The file itself still opens from its own row, even if the
            // content root has moved since it was written.
            var fileName = Path.GetFileName(stored.StoragePath);
            string[] moved =
            [
                $"/srv/old-root/App_Data/contracts/{stored.ContractKey:N}/{fileName}",
                $@"C:\old-root\App_Data\contracts\{stored.ContractKey:N}\{fileName}",
            ];
            foreach (var path in moved)
            {
                await using var stream = await service.OpenReadAsync(stored with { StoragePath = path });
                Assert.StartsWith("%PDF", await new StreamReader(stream).ReadToEndAsync(), StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_stored_url_must_be_this_documents_file_in_the_configured_account()
    {
        Environment.SetEnvironmentVariable("AzureFiles:ConnectionString", "DefaultEndpointsProtocol=https;AccountName=contractfiles;AccountKey=abcd;EndpointSuffix=core.windows.net");
        Environment.SetEnvironmentVariable("AzureFiles:ShareName", "contract-documents");
        try
        {
            var service = new ContractDocumentStorageService(new TestEnvironment(Path.GetTempPath()), new ContractRepositoryStub(), TimeProvider.System);
            var document = Document();
            var ownFile = $"{document.ContractKey:N}/{document.ContractDocumentKey:N}.pdf";

            // Refused before any request is made: another host, another
            // share, or another document's file in the right share.
            string[] refused =
            [
                $"https://attacker.example/contract-documents/{ownFile}",
                $"https://contractfiles.file.core.windows.net/other-share/{ownFile}",
                $"https://contractfiles.file.core.windows.net/contract-documents/{document.ContractKey:N}/{Guid.NewGuid():N}.pdf",
                "https://169.254.169.254/metadata/instance.pdf",
            ];
            foreach (var url in refused)
            {
                await Assert.ThrowsAsync<ContractDocumentUnavailableException>(() => service.OpenReadAsync(document with { StoragePath = url }));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AzureFiles:ConnectionString", null);
            Environment.SetEnvironmentVariable("AzureFiles:ShareName", null);
        }
    }

    [Fact]
    public void Azure_file_share_settings_are_resolved_from_environment_aliases()
    {
        var connectionString = "DefaultEndpointsProtocol=https;AccountName=contractfiles;AccountKey=abcd;EndpointSuffix=core.windows.net";
        var shareName = "contract-documents";

        Environment.SetEnvironmentVariable("AzureFiles:ConnectionString", connectionString);
        Environment.SetEnvironmentVariable("AzureFiles:ShareName", shareName);
        try
        {
            var resolved = ContractDocumentStorageService.TryResolveFileShareSettings(out var resolvedConnectionString, out var resolvedShareName);

            Assert.True(resolved);
            Assert.Equal(connectionString, resolvedConnectionString);
            Assert.Equal(shareName, resolvedShareName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AzureFiles:ConnectionString", null);
            Environment.SetEnvironmentVariable("AzureFiles:ShareName", null);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "contract-storage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static IFormFile PdfFile()
    {
        var bytes = "%PDF-1.4\nexample"u8.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "agreement.pdf");
    }

    private static ContractDocument Document() => new()
    {
        ContractDocumentKey = Guid.NewGuid(), ContractKey = Guid.NewGuid(), DocumentType = ContractDocumentType.SOW,
        FileName = "agreement.pdf", ContentType = "application/pdf", SizeBytes = 16, StoragePath = "placeholder.pdf",
        UploadedAtUtc = DateTime.UtcNow
    };

    private static Contract Contract() => new()
    {
        ContractKey = Guid.NewGuid(), CustomerKey = Guid.NewGuid(), Reference = "test",
        CommercialModel = CommercialModel.FixedPrice, Currency = "GBP",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Status = ContractStatus.Draft, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ContractRepositoryStub : IContractRepository
    {
        public Contract? Contract { get; set; }
        public bool SaveCalled { get; private set; }
        public bool ThrowOnSave { get; set; } = true;

        public Task<Contract?> GetContractByKeyAsync(Guid key, Guid tenantId) => Task.FromResult(Contract);
        public Task<ContractDocument> SaveDocumentAsync(ContractDocument document, Guid tenantId)
        {
            SaveCalled = true;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Simulated database failure");
            }

            return Task.FromResult(document);
        }

        public Task<IReadOnlyList<Contract>> GetContractsAsync(Guid tenantId) => throw new NotImplementedException();
        public Task<Contract> CreateContractAsync(Contract contract, Guid tenantId) => throw new NotImplementedException();
        public Task<Contract> UpdateStatusAsync(Guid key, ContractStatus status, string? notes, Guid tenantId) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContractDocument>> GetDocumentsAsync(Guid key, Guid tenantId) => throw new NotImplementedException();
        public Task<ContractDocument?> GetDocumentAsync(Guid key, Guid documentKey, Guid tenantId) => throw new NotImplementedException();
        public Task<IReadOnlyList<NonLabourCost>> GetNonLabourCostsAsync(Guid key, Guid tenantId) => throw new NotImplementedException();
        public Task<NonLabourCost> AddNonLabourCostAsync(NonLabourCost cost, Guid tenantId) => throw new NotImplementedException();
        public Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid key, Guid tenantId) => throw new NotImplementedException();
        public Task<(Invoice Invoice, IReadOnlyList<InvoiceLine> Lines)?> GetInvoiceAsync(Guid key, Guid invoiceKey, Guid tenantId) => throw new NotImplementedException();
        public Task<Invoice> CreateInvoiceAsync(Invoice invoice, IReadOnlyList<InvoiceLine> lines, Guid tenantId) => throw new NotImplementedException();
        public Task<string?> IssueInvoiceAsync(Guid contractKey, Guid invoiceKey, string contractReference, DateTime issuedAtUtc, Guid tenantId) => throw new NotImplementedException();
        public Task<bool> MoveInvoiceAsync(Guid invoiceKey, InvoiceStatus from, InvoiceStatus to, Guid tenantId) => throw new NotImplementedException();
    }
}
