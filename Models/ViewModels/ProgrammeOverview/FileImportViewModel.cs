using ProgrammePulse.Services.Integrations.FileImport;

namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>The import page: both canonical files' column contracts, plus the outcome of the last upload on this request (if any).</summary>
public sealed record FileImportViewModel(
    IReadOnlyList<DeliveryExportColumn> WorkItemColumns,
    IReadOnlyList<DeliveryExportColumn> TimeEntryColumns,
    DeliveryExportKind? LastKind = null,
    FileImportResult? LastResult = null,
    string? Error = null,
    string? CancelledNotice = null);
