using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Upload a sanitised delivery export instead of connecting a live source —
/// the entry route for a paid diagnostic, where the customer shares files,
/// not credentials. Same gate as managing a connection (Admin,
/// ManageIntegrations), tenant-scoped, rate-limited like a sync, and
/// size-capped before the body is read.
/// </summary>
[Route("staffops/programme/import")]
[RequireFeature(ProductFeature.FileImport)]
[RequireCapability(Capability.ManageIntegrations)]
public sealed class StaffFileImportController(
    ICurrentStaff currentStaff,
    IDeliveryExportImportService importService,
    TimeProvider clock,
    ILogger<StaffFileImportController> logger) : Controller
{
    public const long MaxUploadBytes = 5 * 1024 * 1024;
    private const string ViewPath = "~/Views/StaffOps/Programme/Import.cshtml";

    [HttpGet("")]
    public IActionResult Index([CurrentTenant] Guid tenantId)
    {
        return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns));
    }

    [HttpGet("template/{kind}")]
    public IActionResult Template(string kind)
    {
        if (!TryParseKind(kind, out var parsed))
            return NotFound();

        var fileName = parsed == DeliveryExportKind.WorkItems ? "work-items-template.csv" : "time-entries-template.csv";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(DeliveryExportSchema.TemplateCsv(parsed))).ToArray(),
            "text/csv", fileName);
    }

    /// <summary>
    /// A fictional agency's exports for a first conversation, so a prospect
    /// sees the Evidence Check work before sharing anything of their own.
    /// Import it into a demonstration tenant, never a customer's.
    /// </summary>
    [HttpGet("sample/{kind}")]
    public IActionResult Sample(string kind)
    {
        if (!TryParseKind(kind, out var parsed))
            return NotFound();

        var fileName = parsed == DeliveryExportKind.WorkItems ? "sample-work-items.csv" : "sample-time-entries.csv";
        var csv = DeliveryExportSample.Csv(parsed, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
    }

    [HttpPost("{kind}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sync")]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + 64 * 1024)]
    public async Task<IActionResult> Upload([CurrentTenant] Guid tenantId, string kind, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!TryParseKind(kind, out var parsed))
            return NotFound();

        if (file is null || file.Length == 0)
            return Page(Rejected(parsed, "Choose a CSV file to upload."));
        if (file.Length > MaxUploadBytes)
            return Page(Rejected(parsed, $"The file is larger than {MaxUploadBytes / (1024 * 1024)} MB. Split it and upload each part."));
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return Page(Rejected(parsed, "Upload a .csv file. From Excel, use File → Save As → CSV UTF-8."));

        string text;
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            return Page(Rejected(parsed, "The file isn't UTF-8 text. From Excel, use File → Save As → CSV UTF-8."));
        }

        try
        {
            var memberId = await currentStaff.GetMemberIdAsync();
            var result = await importService.ImportAsync(tenantId, parsed, text, memberId, cancellationToken);
            Services.Security.SecurityEvents.Write(logger, HttpContext, result.Imported ? "FileImportCompleted" : "FileImportRejected",
                tenantId: tenantId, provider: DeliveryExportImportService.SourceName, warning: false);
            return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns, parsed, result));
        }
        catch (InvalidOperationException ex)
        {
            return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns, parsed, Error: ex.Message));
        }
    }

    /// <summary>
    /// Applies a replacement the person has just previewed. The service
    /// re-validates and refuses if the removals are no longer what was shown.
    /// </summary>
    [HttpPost("confirm/{stagingKey:guid}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sync")]
    public async Task<IActionResult> Confirm([CurrentTenant] Guid tenantId, Guid stagingKey, CancellationToken cancellationToken)
    {
        try
        {
            var result = await importService.ConfirmAsync(tenantId, stagingKey, await currentStaff.GetMemberIdAsync(), cancellationToken);
            Services.Security.SecurityEvents.Write(logger, HttpContext, result.Imported ? "FileImportCompleted" : "FileImportRejected",
                tenantId: tenantId, provider: DeliveryExportImportService.SourceName, warning: false);
            return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns, result.Kind, result));
        }
        catch (InvalidOperationException ex)
        {
            return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns, Error: ex.Message));
        }
    }

    [HttpPost("cancel/{stagingKey:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([CurrentTenant] Guid tenantId, Guid stagingKey)
    {
        await importService.CancelAsync(tenantId, stagingKey, await currentStaff.GetMemberIdAsync());
        return Page(new FileImportViewModel(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns,
            Error: null, CancelledNotice: "Import cancelled. Nothing was changed."));
    }

    private static FileImportViewModel Rejected(DeliveryExportKind kind, string message) =>
        new(DeliveryExportSchema.WorkItemColumns, DeliveryExportSchema.TimeEntryColumns, kind, FileImportResult.Rejected(message));

    private IActionResult Page(FileImportViewModel model)
    {
        ViewData["Title"] = "Import";
        return View(ViewPath, model);
    }

    private static bool TryParseKind(string? value, out DeliveryExportKind kind)
    {
        kind = default;
        switch (value?.ToLowerInvariant())
        {
            case "work-items": kind = DeliveryExportKind.WorkItems; return true;
            case "time-entries": kind = DeliveryExportKind.TimeEntries; return true;
            default: return false;
        }
    }
}
