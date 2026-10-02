using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>The new-contract form as submitted.</summary>
public sealed record ContractInput(
    Guid CustomerKey,
    string? Reference,
    CommercialModel CommercialModel,
    decimal? TotalContractValue,
    decimal? AnnualValue,
    decimal? BillRate,
    string? Currency,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>A readable contract document and its content, for download.</summary>
public sealed record ContractDocumentDownload(ContractDocument Document, Stream Content);

/// <summary>
/// Contract Ops commands and page read models: contracts, status, documents,
/// non-labour costs and invoices. Every change and every document download
/// is audited here. Moved out of StaffContractController.
///
/// Audit detail is serialised JSON. It used to be string interpolation,
/// e.g. <c>$"{{\"reference\":\"{reference}\"}}"</c>, so a reference or file
/// name containing a quote wrote malformed JSON into the audit trail.
/// </summary>
public interface IContractAdminService
{
    Task<ContractsIndexPageViewModel> BuildIndexAsync(Guid tenantId);

    Task<(CommandOutcome Outcome, Guid? ContractKey)> CreateAsync(Guid tenantId, ContractInput input, int? actorMemberId);

    Task<ContractDetailPageViewModel?> BuildDetailAsync(Guid tenantId, Guid contractKey);

    Task UpdateStatusAsync(Guid tenantId, Guid contractKey, ContractStatus status, string? notes, int? actorMemberId);

    /// <summary>Null on success; otherwise the reason the file was refused.</summary>
    Task<string?> UploadDocumentAsync(Guid tenantId, Guid contractKey, IFormFile file, ContractDocumentType documentType, Guid? uploadedByStaffKey, int? actorMemberId);

    /// <summary>Null when the document does not exist in this tenant or its file is unavailable. A download is audited once opened.</summary>
    Task<ContractDocumentDownload?> OpenDocumentAsync(Guid tenantId, Guid contractKey, Guid documentKey, int? actorMemberId);

    Task<CommandOutcome> AddNonLabourCostAsync(Guid tenantId, Guid contractKey, string? description, decimal amount, string? currency, DateOnly incurredOn, Guid? recordedByStaffKey, int? actorMemberId);

    /// <summary>Null on success; otherwise the reason no invoice was generated.</summary>
    Task<string?> GenerateInvoiceAsync(Guid tenantId, Guid contractKey, DateOnly periodStart, DateOnly periodEnd, Guid? generatedByStaffKey, int? actorMemberId);

    Task<InvoiceDocumentPageViewModel?> BuildInvoiceDocumentAsync(Guid tenantId, Guid contractKey, Guid invoiceKey);

    /// <summary>
    /// Moves an invoice along its lifecycle (Draft → Issued or Discarded,
    /// Issued → Paid). Issuing assigns the invoice number and date, and is
    /// refused while hours of unknown billability are unconfirmed.
    /// </summary>
    Task<CommandOutcome> ChangeInvoiceStatusAsync(Guid tenantId, Guid contractKey, Guid invoiceKey, InvoiceStatus status, bool needsReviewHoursConfirmed, int? actorMemberId);
}

public sealed class ContractAdminService(
    IContractRepository contractRepository,
    IProgrammeReadRepository programmeRepository,
    IContractCommercialService contractCommercialService,
    IContractDocumentStorageService documentStorage,
    IInvoiceGenerationService invoiceGeneration,
    IContractAuditLogRepository audit,
    TimeProvider timeProvider) : IContractAdminService
{
    public const string ReferenceAndCurrencyRequired = "Reference and currency are required.";
    public const string DescriptionAndCurrencyRequired = "Description and currency are required.";

    public async Task<ContractsIndexPageViewModel> BuildIndexAsync(Guid tenantId) =>
        new((await contractCommercialService.BuildSummaryAsync(tenantId)).Rows, await programmeRepository.GetCustomersAsync(tenantId));

    public async Task<(CommandOutcome Outcome, Guid? ContractKey)> CreateAsync(Guid tenantId, ContractInput input, int? actorMemberId)
    {
        if (string.IsNullOrWhiteSpace(input.Reference) || string.IsNullOrWhiteSpace(input.Currency))
        {
            return (CommandOutcome.Invalid(ReferenceAndCurrencyRequired), null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var contract = await contractRepository.CreateContractAsync(new Contract
        {
            ContractKey = Guid.NewGuid(),
            CustomerKey = input.CustomerKey,
            Reference = input.Reference.Trim(),
            CommercialModel = input.CommercialModel,
            TotalContractValue = input.TotalContractValue,
            AnnualValue = input.AnnualValue,
            BillRate = input.BillRate,
            Currency = input.Currency.Trim().ToUpperInvariant(),
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Status = ContractStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);

        await LogAsync("Contract", contract.ContractKey, "Created", new { reference = contract.Reference }, actorMemberId, tenantId);
        return (CommandOutcome.Ok, contract.ContractKey);
    }

    public async Task<ContractDetailPageViewModel?> BuildDetailAsync(Guid tenantId, Guid contractKey)
    {
        var contract = await contractRepository.GetContractByKeyAsync(contractKey, tenantId);
        if (contract is null)
        {
            return null;
        }

        return new ContractDetailPageViewModel(
            await contractCommercialService.BuildCommercialPositionAsync(contractKey, tenantId),
            contract,
            await contractRepository.GetDocumentsAsync(contractKey, tenantId),
            await contractRepository.GetInvoicesAsync(contractKey, tenantId));
    }

    public async Task UpdateStatusAsync(Guid tenantId, Guid contractKey, ContractStatus status, string? notes, int? actorMemberId)
    {
        await contractRepository.UpdateStatusAsync(contractKey, status, notes, tenantId);
        await LogAsync("Contract", contractKey, "StatusChanged", new { status = status.ToString() }, actorMemberId, tenantId);
    }

    public async Task<string?> UploadDocumentAsync(Guid tenantId, Guid contractKey, IFormFile file, ContractDocumentType documentType, Guid? uploadedByStaffKey, int? actorMemberId)
    {
        try
        {
            var document = await documentStorage.StoreAsync(contractKey, file, documentType, uploadedByStaffKey, tenantId);
            await LogAsync("ContractDocument", document.ContractDocumentKey, "Uploaded", new { contractKey, fileName = document.FileName }, actorMemberId, tenantId);
            return null;
        }
        catch (ContractDocumentValidationException ex)
        {
            return ex.Message;
        }
    }

    public async Task<ContractDocumentDownload?> OpenDocumentAsync(Guid tenantId, Guid contractKey, Guid documentKey, int? actorMemberId)
    {
        var document = await contractRepository.GetDocumentAsync(contractKey, documentKey, tenantId);
        if (document is null)
        {
            return null;
        }

        Stream content;
        try
        {
            content = await documentStorage.OpenReadAsync(document);
        }
        catch (ContractDocumentUnavailableException)
        {
            return null;
        }

        await LogAsync("ContractDocument", document.ContractDocumentKey, "Downloaded", null, actorMemberId, tenantId);
        return new ContractDocumentDownload(document, content);
    }

    public async Task<CommandOutcome> AddNonLabourCostAsync(Guid tenantId, Guid contractKey, string? description, decimal amount, string? currency, DateOnly incurredOn, Guid? recordedByStaffKey, int? actorMemberId)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(currency))
        {
            return CommandOutcome.Invalid(DescriptionAndCurrencyRequired);
        }

        var cost = await contractRepository.AddNonLabourCostAsync(new NonLabourCost
        {
            NonLabourCostKey = Guid.NewGuid(),
            ContractKey = contractKey,
            Description = description.Trim(),
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            IncurredOn = incurredOn,
            RecordedByStaffKey = recordedByStaffKey,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        }, tenantId);

        await LogAsync("NonLabourCost", cost.NonLabourCostKey, "Added", new { contractKey, amount = cost.Amount }, actorMemberId, tenantId);
        return CommandOutcome.Ok;
    }

    public async Task<string?> GenerateInvoiceAsync(Guid tenantId, Guid contractKey, DateOnly periodStart, DateOnly periodEnd, Guid? generatedByStaffKey, int? actorMemberId)
    {
        try
        {
            var invoice = await invoiceGeneration.GenerateAsync(contractKey, periodStart, periodEnd, generatedByStaffKey, tenantId);
            await LogAsync("Invoice", invoice.InvoiceKey, "DraftGenerated",
                new { contractKey, subtotal = invoice.Subtotal, needsReviewHours = invoice.NeedsReviewHours }, actorMemberId, tenantId);
            return null;
        }
        catch (InvoiceGenerationException ex)
        {
            return ex.Message;
        }
    }

    public async Task<InvoiceDocumentPageViewModel?> BuildInvoiceDocumentAsync(Guid tenantId, Guid contractKey, Guid invoiceKey)
    {
        var contract = await contractRepository.GetContractByKeyAsync(contractKey, tenantId);
        var result = await contractRepository.GetInvoiceAsync(contractKey, invoiceKey, tenantId);
        if (contract is null || result is null)
        {
            return null;
        }

        var customers = await programmeRepository.GetCustomersAsync(tenantId);
        var customerName = customers.FirstOrDefault(c => c.CustomerKey == contract.CustomerKey)?.Name ?? "(unknown customer)";
        return new InvoiceDocumentPageViewModel(result.Value.Invoice, contract, customerName, result.Value.Lines);
    }

    public async Task<CommandOutcome> ChangeInvoiceStatusAsync(Guid tenantId, Guid contractKey, Guid invoiceKey, InvoiceStatus status, bool needsReviewHoursConfirmed, int? actorMemberId)
    {
        var contract = await contractRepository.GetContractByKeyAsync(contractKey, tenantId);
        var found = await contractRepository.GetInvoiceAsync(contractKey, invoiceKey, tenantId);
        if (contract is null || found is null)
        {
            throw new CrossTenantReferenceException("Invoice", invoiceKey);
        }

        var invoice = found.Value.Invoice;
        if (!InvoiceLifecycle.CanMove(invoice.Status, status))
        {
            return CommandOutcome.Invalid($"An invoice that is {invoice.Status} can't be marked {status}.");
        }

        if (status == InvoiceStatus.Issued)
        {
            if (invoice.NeedsReviewHours > 0m && !needsReviewHoursConfirmed)
            {
                return CommandOutcome.Invalid(
                    $"{invoice.NeedsReviewHours:0.##} hours in this period have no confirmed billability and are not on the invoice. " +
                    "Confirm you have reviewed them, or discard the draft and fix billability at the source.");
            }

            var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
            var number = await contractRepository.IssueInvoiceAsync(contractKey, invoiceKey, contract.Reference, issuedAt, tenantId);
            if (number is null)
            {
                return CommandOutcome.Invalid("That draft was changed by someone else. Reload and try again.");
            }

            await LogAsync("Invoice", invoiceKey, "Issued", new
            {
                contractKey,
                invoiceNumber = number,
                subtotal = invoice.Subtotal,
                needsReviewHours = invoice.NeedsReviewHours,
                needsReviewHoursConfirmed = invoice.NeedsReviewHours > 0m && needsReviewHoursConfirmed
            }, actorMemberId, tenantId);
            return CommandOutcome.Ok;
        }

        if (!await contractRepository.MoveInvoiceAsync(invoiceKey, invoice.Status, status, tenantId))
        {
            return CommandOutcome.Invalid("That invoice was changed by someone else. Reload and try again.");
        }

        await LogAsync("Invoice", invoiceKey, "StatusChanged", new { from = invoice.Status.ToString(), to = status.ToString() }, actorMemberId, tenantId);
        return CommandOutcome.Ok;
    }

    private Task LogAsync(string entityType, Guid entityId, string action, object? detail, int? actorMemberId, Guid tenantId) =>
        audit.LogAsync(entityType, entityId.ToString(), action, actorMemberId,
            detail is null ? null : JsonSerializer.Serialize(detail), timeProvider.GetUtcNow().UtcDateTime, tenantId);
}
