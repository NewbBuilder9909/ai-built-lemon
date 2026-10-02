using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

public sealed class ContractRepository(IScopeProvider scopeProvider) : IContractRepository
{
    public async Task<IReadOnlyList<Contract>> GetContractsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ContractDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<Contract?> GetContractByKeyAsync(Guid contractKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<ContractDto>(
            Sql.Builder.Where("contractKey = @0 AND tenantId = @1", contractKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<Contract> CreateContractAsync(Contract contract, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<CustomerDto>(scope.Database, "customerKey", contract.CustomerKey, d => d.TenantId, tenantId, "Customer");

        var dto = new ContractDto
        {
            ContractKey = contract.ContractKey,
            TenantId = tenantId,
            CustomerKey = contract.CustomerKey,
            Reference = contract.Reference,
            CommercialModel = contract.CommercialModel.ToString(),
            TotalContractValue = contract.TotalContractValue,
            AnnualValue = contract.AnnualValue,
            BillRate = contract.BillRate,
            Currency = contract.Currency,
            StartDate = contract.StartDate.ToDateTime(TimeOnly.MinValue),
            EndDate = contract.EndDate.ToDateTime(TimeOnly.MinValue),
            Status = contract.Status.ToString(),
            Notes = contract.Notes,
            CreatedAtUtc = contract.CreatedAtUtc,
            UpdatedAtUtc = contract.UpdatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<Contract> UpdateStatusAsync(Guid contractKey, ContractStatus status, string? notes, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var dto = await scope.Database.FirstOrDefaultAsync<ContractDto>(
            Sql.Builder.Where("contractKey = @0 AND tenantId = @1", contractKey, tenantId))
            ?? throw new CrossTenantReferenceException("Contract", contractKey);

        dto.Status = status.ToString();
        dto.Notes = notes;
        dto.UpdatedAtUtc = DateTime.UtcNow;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<IReadOnlyList<ContractDocument>> GetDocumentsAsync(Guid contractKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<ContractDocumentDto>(
            Sql.Builder.Where("contractKey = @0 AND tenantId = @1", contractKey, tenantId).OrderBy("uploadedAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<ContractDocument?> GetDocumentAsync(Guid contractKey, Guid contractDocumentKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<ContractDocumentDto>(
            Sql.Builder.Where("contractKey = @0 AND contractDocumentKey = @1 AND tenantId = @2", contractKey, contractDocumentKey, tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<ContractDocument> SaveDocumentAsync(ContractDocument document, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ContractDto>(scope.Database, "contractKey", document.ContractKey, d => d.TenantId, tenantId, "Contract");

        var dto = new ContractDocumentDto
        {
            ContractDocumentKey = document.ContractDocumentKey,
            TenantId = tenantId,
            ContractKey = document.ContractKey,
            DocumentType = document.DocumentType.ToString(),
            FileName = document.FileName,
            ContentType = document.ContentType,
            SizeBytes = document.SizeBytes,
            StoragePath = document.StoragePath,
            UploadedByStaffKey = document.UploadedByStaffKey,
            UploadedAtUtc = document.UploadedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<IReadOnlyList<NonLabourCost>> GetNonLabourCostsAsync(Guid contractKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<NonLabourCostDto>(
            Sql.Builder.Where("contractKey = @0 AND tenantId = @1", contractKey, tenantId).OrderBy("incurredOn DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<NonLabourCost> AddNonLabourCostAsync(NonLabourCost cost, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ContractDto>(scope.Database, "contractKey", cost.ContractKey, d => d.TenantId, tenantId, "Contract");

        var dto = new NonLabourCostDto
        {
            NonLabourCostKey = cost.NonLabourCostKey,
            TenantId = tenantId,
            ContractKey = cost.ContractKey,
            Description = cost.Description,
            Amount = cost.Amount,
            Currency = cost.Currency,
            IncurredOn = cost.IncurredOn.ToDateTime(TimeOnly.MinValue),
            RecordedByStaffKey = cost.RecordedByStaffKey,
            CreatedAtUtc = cost.CreatedAtUtc
        };

        await scope.Database.InsertAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid contractKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<InvoiceDto>(
            Sql.Builder.Where("contractKey = @0 AND tenantId = @1", contractKey, tenantId).OrderBy("createdAtUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<(Invoice Invoice, IReadOnlyList<InvoiceLine> Lines)?> GetInvoiceAsync(Guid contractKey, Guid invoiceKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<InvoiceDto>(
            Sql.Builder.Where("contractKey = @0 AND invoiceKey = @1 AND tenantId = @2", contractKey, invoiceKey, tenantId));
        if (dto is null)
        {
            return null;
        }

        var lineDtos = await scope.Database.FetchAsync<InvoiceLineDto>(
            Sql.Builder.Where("invoiceKey = @0", invoiceKey));

        return (Map(dto), lineDtos.Select(Map).ToList());
    }

    public async Task<Invoice> CreateInvoiceAsync(Invoice invoice, IReadOnlyList<InvoiceLine> lines, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await EnsureBelongsToTenantAsync<ContractDto>(scope.Database, "contractKey", invoice.ContractKey, d => d.TenantId, tenantId, "Contract");

        var dto = new InvoiceDto
        {
            InvoiceKey = invoice.InvoiceKey,
            TenantId = tenantId,
            ContractKey = invoice.ContractKey,
            InvoiceNumber = invoice.InvoiceNumber,
            PeriodStart = invoice.PeriodStart.ToDateTime(TimeOnly.MinValue),
            PeriodEnd = invoice.PeriodEnd.ToDateTime(TimeOnly.MinValue),
            Currency = invoice.Currency,
            Subtotal = invoice.Subtotal,
            Status = invoice.Status.ToString(),
            GeneratedByStaffKey = invoice.GeneratedByStaffKey,
            CreatedAtUtc = invoice.CreatedAtUtc,
            IssuedAtUtc = invoice.IssuedAtUtc,
            NeedsReviewHours = invoice.NeedsReviewHours
        };

        await scope.Database.InsertAsync(dto);

        foreach (var line in lines)
        {
            await scope.Database.InsertAsync(new InvoiceLineDto
            {
                InvoiceLineKey = line.InvoiceLineKey,
                TenantId = tenantId,
                InvoiceKey = invoice.InvoiceKey,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitRate = line.UnitRate,
                LineTotal = line.LineTotal
            });
        }

        scope.Complete();

        return Map(dto);
    }

    public async Task<string?> IssueInvoiceAsync(Guid contractKey, Guid invoiceKey, string contractReference, DateTime issuedAtUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        // UPDLOCK + HOLDLOCK on the contract's numbered invoices serialises
        // concurrent issues for one contract, so the count and the update
        // below can't interleave and hand out the same number twice.
        var numbered = await scope.Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM [{InvoiceDto.TableName}] WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE contractKey = @0 AND tenantId = @1 AND status IN (@2, @3)",
            contractKey, tenantId, nameof(InvoiceStatus.Issued), nameof(InvoiceStatus.Paid));

        var number = $"{contractReference}-{numbered + 1:D3}";
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{InvoiceDto.TableName}] SET status = @0, invoiceNumber = @1, issuedAtUtc = @2 " +
            "WHERE invoiceKey = @3 AND contractKey = @4 AND tenantId = @5 AND status = @6",
            nameof(InvoiceStatus.Issued), number, issuedAtUtc, invoiceKey, contractKey, tenantId, nameof(InvoiceStatus.Draft));

        scope.Complete();
        return updated == 1 ? number : null;
    }

    public async Task<bool> MoveInvoiceAsync(Guid invoiceKey, InvoiceStatus from, InvoiceStatus to, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var updated = await scope.Database.ExecuteAsync(
            $"UPDATE [{InvoiceDto.TableName}] SET status = @0 WHERE invoiceKey = @1 AND tenantId = @2 AND status = @3",
            to.ToString(), invoiceKey, tenantId, from.ToString());
        scope.Complete();
        return updated == 1;
    }

    /// <summary>
    /// Same guard as ProgrammeRepository.EnsureBelongsToTenantAsync — a
    /// caller-supplied foreign key (Contract.CustomerKey into
    /// ProgrammeOps_Customer, or a document/cost/invoice's ContractKey into
    /// ContractOps_Contract) is looked up by its own key and rejected unless
    /// it belongs to the same tenant, so a controller-supplied key from
    /// another tenant is never silently accepted.
    /// </summary>
    private static async Task EnsureBelongsToTenantAsync<TDto>(IUmbracoDatabase database, string keyColumn, Guid key, Func<TDto, Guid?> tenantIdSelector, Guid tenantId, string entityName)
        where TDto : class
    {
        var parent = await database.FirstOrDefaultAsync<TDto>(Sql.Builder.Where($"{SqlIdentifier.Quote(keyColumn)} = @0", key));
        if (parent is null || tenantIdSelector(parent) != tenantId)
        {
            throw new CrossTenantReferenceException(entityName, key);
        }
    }

    private static Invoice Map(InvoiceDto dto) => new()
    {
        InvoiceKey = dto.InvoiceKey,
        TenantId = dto.TenantId,
        ContractKey = dto.ContractKey,
        InvoiceNumber = dto.InvoiceNumber,
        PeriodStart = DateOnly.FromDateTime(dto.PeriodStart),
        PeriodEnd = DateOnly.FromDateTime(dto.PeriodEnd),
        Currency = dto.Currency,
        Subtotal = dto.Subtotal,
        Status = Enum.Parse<InvoiceStatus>(dto.Status),
        GeneratedByStaffKey = dto.GeneratedByStaffKey,
        CreatedAtUtc = dto.CreatedAtUtc,
        IssuedAtUtc = dto.IssuedAtUtc,
        NeedsReviewHours = dto.NeedsReviewHours
    };

    private static InvoiceLine Map(InvoiceLineDto dto) => new()
    {
        InvoiceLineKey = dto.InvoiceLineKey,
        TenantId = dto.TenantId,
        InvoiceKey = dto.InvoiceKey,
        Description = dto.Description,
        Quantity = dto.Quantity,
        UnitRate = dto.UnitRate,
        LineTotal = dto.LineTotal
    };

    private static NonLabourCost Map(NonLabourCostDto dto) => new()
    {
        NonLabourCostKey = dto.NonLabourCostKey,
        TenantId = dto.TenantId,
        ContractKey = dto.ContractKey,
        Description = dto.Description,
        Amount = dto.Amount,
        Currency = dto.Currency,
        IncurredOn = DateOnly.FromDateTime(dto.IncurredOn),
        RecordedByStaffKey = dto.RecordedByStaffKey,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    private static Contract Map(ContractDto dto) => new()
    {
        ContractKey = dto.ContractKey,
        TenantId = dto.TenantId,
        CustomerKey = dto.CustomerKey,
        Reference = dto.Reference,
        CommercialModel = Enum.Parse<CommercialModel>(dto.CommercialModel),
        TotalContractValue = dto.TotalContractValue,
        AnnualValue = dto.AnnualValue,
        BillRate = dto.BillRate,
        Currency = dto.Currency,
        StartDate = DateOnly.FromDateTime(dto.StartDate),
        EndDate = DateOnly.FromDateTime(dto.EndDate),
        Status = Enum.Parse<ContractStatus>(dto.Status),
        Notes = dto.Notes,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };

    private static ContractDocument Map(ContractDocumentDto dto) => new()
    {
        ContractDocumentKey = dto.ContractDocumentKey,
        TenantId = dto.TenantId,
        ContractKey = dto.ContractKey,
        DocumentType = Enum.Parse<ContractDocumentType>(dto.DocumentType),
        FileName = dto.FileName,
        ContentType = dto.ContentType,
        SizeBytes = dto.SizeBytes,
        StoragePath = dto.StoragePath,
        UploadedByStaffKey = dto.UploadedByStaffKey,
        UploadedAtUtc = dto.UploadedAtUtc
    };
}
