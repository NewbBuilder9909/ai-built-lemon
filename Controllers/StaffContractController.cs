using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Commercial control: contract terms, the document repository, and the
/// value-vs-cost burn-down. Admin-only on every single action — this is
/// commercially sensitive data, arguably more sensitive than the cost/rate
/// data StaffAdminController already gates the same way. No page here is
/// reachable by Team Lead, unlike the Reporting Hub.
///
/// Also plan-gated: [RequireFeature(ContractOps)] means a tenant on a plan
/// without Contract Ops gets a 403 "not in your plan" page before any
/// action runs. That is a commercial control layered on top of — never a
/// replacement for — the [RequireCapability] on every action. The commands,
/// read models and audit trail live in IContractAdminService.
/// </summary>
[Route("staffops/contracts")]
[RequireFeature(ProductFeature.ContractOps)]
[RequireModule(ProductModules.Contracts)]
public sealed class StaffContractController(
    ICurrentStaff currentStaff,
    IContractAdminService contracts,
    IPortfolioMarginService portfolioMarginService) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await contracts.BuildIndexAsync(tenantId);

        ViewData["Title"] = "Commercial Position";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Contracts/Index.cshtml", model);
    }

    [HttpGet("overview")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Overview([CurrentTenant] Guid tenantId)
    {
        var portfolio = await portfolioMarginService.BuildAsync(tenantId);

        ViewData["Title"] = "Commercial Overview";
        return View("~/Views/StaffOps/Contracts/Overview.cshtml", portfolio);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Create([CurrentTenant] Guid tenantId,
        Guid customerKey, string? reference, CommercialModel commercialModel,
        decimal? totalContractValue, decimal? annualValue, decimal? billRate,
        string? currency, DateOnly startDate, DateOnly endDate)
    {
        var (outcome, contractKey) = await contracts.CreateAsync(tenantId,
            new ContractInput(customerKey, reference, commercialModel, totalContractValue, annualValue, billRate, currency, startDate, endDate),
            await currentStaff.GetMemberIdAsync());

        return outcome.Succeeded
            ? Redirect($"/staffops/contracts/{contractKey}")
            : Redirect("/staffops/contracts?message=" + Uri.EscapeDataString(outcome.Error!));
    }

    [HttpGet("{contractKey:guid}")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> Detail([CurrentTenant] Guid tenantId, Guid contractKey, string? message = null)
    {
        var model = await contracts.BuildDetailAsync(tenantId, contractKey);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Contract — {model.Contract.Reference}";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Contracts/Detail.cshtml", model);
    }

    [HttpPost("{contractKey:guid}/status")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> UpdateStatus([CurrentTenant] Guid tenantId, Guid contractKey, ContractStatus status, string? notes)
    {
        await contracts.UpdateStatusAsync(tenantId, contractKey, status, notes, await currentStaff.GetMemberIdAsync());
        return BackToContract(contractKey);
    }

    // Refused before the body is buffered; the service caps a document at 25 MB.
    [HttpPost("{contractKey:guid}/documents")]
    [RequestSizeLimit(25 * 1024 * 1024 + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024 + 64 * 1024)]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> UploadDocument([CurrentTenant] Guid tenantId, Guid contractKey, IFormFile file, ContractDocumentType documentType)
    {
        var error = await contracts.UploadDocumentAsync(tenantId, contractKey, file, documentType,
            (await currentStaff.GetProfileAsync())?.StaffKey, await currentStaff.GetMemberIdAsync());
        return BackToContract(contractKey, error);
    }

    [HttpGet("{contractKey:guid}/documents/{documentKey:guid}")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> DownloadDocument([CurrentTenant] Guid tenantId, Guid contractKey, Guid documentKey)
    {
        var download = await contracts.OpenDocumentAsync(tenantId, contractKey, documentKey, await currentStaff.GetMemberIdAsync());
        return download is null
            ? NotFound()
            : File(download.Content, download.Document.ContentType, download.Document.FileName);
    }

    [HttpPost("{contractKey:guid}/costs")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> AddNonLabourCost([CurrentTenant] Guid tenantId, Guid contractKey, string? description, decimal amount, string? currency, DateOnly incurredOn)
    {
        var outcome = await contracts.AddNonLabourCostAsync(tenantId, contractKey, description, amount, currency, incurredOn,
            (await currentStaff.GetProfileAsync())?.StaffKey, await currentStaff.GetMemberIdAsync());
        return BackToContract(contractKey, outcome.Error);
    }

    [HttpPost("{contractKey:guid}/invoices")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> GenerateInvoice([CurrentTenant] Guid tenantId, Guid contractKey, DateOnly periodStart, DateOnly periodEnd)
    {
        var error = await contracts.GenerateInvoiceAsync(tenantId, contractKey, periodStart, periodEnd,
            (await currentStaff.GetProfileAsync())?.StaffKey, await currentStaff.GetMemberIdAsync());
        return BackToContract(contractKey, error);
    }

    [HttpGet("{contractKey:guid}/invoices/{invoiceKey:guid}")]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> InvoiceDocument([CurrentTenant] Guid tenantId, Guid contractKey, Guid invoiceKey)
    {
        var model = await contracts.BuildInvoiceDocumentAsync(tenantId, contractKey, invoiceKey);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["Title"] = model.Invoice.HasNumber ? $"Invoice {model.Invoice.InvoiceNumber}" : "Draft invoice";
        return View("~/Views/StaffOps/Contracts/InvoiceDocument.cshtml", model);
    }

    [HttpPost("{contractKey:guid}/invoices/{invoiceKey:guid}/status")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContracts)]
    public async Task<IActionResult> UpdateInvoiceStatus([CurrentTenant] Guid tenantId, Guid contractKey, Guid invoiceKey, InvoiceStatus status, bool needsReviewHoursConfirmed = false)
    {
        var outcome = await contracts.ChangeInvoiceStatusAsync(tenantId, contractKey, invoiceKey, status, needsReviewHoursConfirmed, await currentStaff.GetMemberIdAsync());
        return BackToContract(contractKey, outcome.Error);
    }

    private IActionResult BackToContract(Guid contractKey, string? message = null) =>
        message is null
            ? Redirect($"/staffops/contracts/{contractKey}")
            : Redirect($"/staffops/contracts/{contractKey}?message=" + Uri.EscapeDataString(message));
}
