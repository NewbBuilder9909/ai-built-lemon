using System.Globalization;
using ProgrammePulse.Models.Erp;
using ProgrammePulse.Models.ViewModels;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Turns already-processed ErpPipelineItems into presentation-ready view models.
/// Everything display-shaped (labels, CSS classes, formatted numbers/dates) is
/// decided here so the Razor views only need to loop and print — no branching.
/// </summary>
public sealed class DashboardAggregationService : IDashboardAggregationService
{
    private static readonly ProcessingStage[] PipelineOrder =
    [
        ProcessingStage.New,
        ProcessingStage.Validating,
        ProcessingStage.Transformed,
        ProcessingStage.Exception
    ];

    public OperationsHubDashboardViewModel BuildDashboard(IReadOnlyList<ErpPipelineItem> items)
    {
        var total = items.Count;
        var transformed = items.Count(i => i.Stage == ProcessingStage.Transformed);
        var requiringAttention = total - transformed;
        var completionPercent = total == 0 ? 0 : (int)Math.Round(transformed * 100m / total);

        var kpiCards = new List<KpiCardViewModel>
        {
            new("Total Records", total.ToString(CultureInfo.InvariantCulture)),
            new("Successfully Transformed", transformed.ToString(CultureInfo.InvariantCulture)),
            new("Requiring Attention", requiringAttention.ToString(CultureInfo.InvariantCulture)),
            new("Completion", $"{completionPercent}%")
        };

        var stageCounts = PipelineOrder
            .Select(stage =>
            {
                var count = items.Count(i => i.Stage == stage);
                var (label, cssClass) = StatusPresentation.For(stage);
                var percent = total == 0 ? 0 : (int)Math.Round(count * 100m / total);
                return (stage, label, cssClass, count, percent);
            })
            .ToList();

        var statusSummary = new StatusSummaryViewModel(
            stageCounts.Select(s => new StatusSummaryItemViewModel(s.label, s.cssClass, s.count, s.percent)).ToList());

        var pipeline = new PipelinePanelViewModel(
            stageCounts.Select(s => new PipelineStageViewModel(s.label, s.cssClass, s.count, s.percent)).ToList());

        var recentActivity = BuildRecentActivity(items);

        return new OperationsHubDashboardViewModel(kpiCards, statusSummary, pipeline, recentActivity);
    }

    public ErpRecordsListViewModel BuildRecordsList(
        IReadOnlyList<ErpPipelineItem> items,
        string? search,
        string? statusFilter,
        string? validationFilter,
        string? sortColumn,
        string? sortDirection)
    {
        var filtered = items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(i =>
                i.Raw.SourceErpId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Transformed.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Transformed.WorkType.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<NormalisedStatus>(statusFilter, ignoreCase: true, out var wantedStatus))
        {
            filtered = filtered.Where(i => i.Transformed.Status == wantedStatus);
        }

        if (!string.IsNullOrWhiteSpace(validationFilter))
        {
            filtered = validationFilter.ToLowerInvariant() switch
            {
                "valid" => filtered.Where(i => !i.Validation.HasErrors && !i.Validation.HasWarnings),
                "warning" => filtered.Where(i => i.Validation.HasWarnings && !i.Validation.HasErrors),
                "error" => filtered.Where(i => i.Validation.HasErrors),
                _ => filtered
            };
        }

        var column = string.IsNullOrWhiteSpace(sortColumn) ? "erpId" : sortColumn;
        var direction = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        var sorted = Sort(filtered, column, direction);

        var rows = sorted.Select(ToRow).ToList();

        return new ErpRecordsListViewModel(
            rows,
            items.Count,
            search,
            BuildStatusFilterOptions(statusFilter),
            BuildValidationFilterOptions(validationFilter),
            column,
            direction);
    }

    public ErpRecordDetailViewModel? BuildDetail(IReadOnlyList<ErpPipelineItem> items, string erpId)
    {
        var item = items.FirstOrDefault(i => string.Equals(i.Raw.SourceErpId, erpId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return null;
        }

        var validation = BuildValidationSummary(item.Validation);
        var (statusLabel, statusCss) = StatusPresentation.For(item.Transformed.Status);
        var (stageLabel, stageCss) = StatusPresentation.For(item.Stage);

        return new ErpRecordDetailViewModel(
            item.Raw.SourceErpId,
            item.Transformed.CustomerName,
            item.Transformed.WorkType,
            item.Raw.RawStatus ?? "—",
            statusLabel,
            statusCss,
            FormatAmount(item.Transformed.Amount),
            item.Raw.RawSourceDate ?? "—",
            FormatUtcDate(item.Transformed.SourceDateUtc),
            validation,
            stageLabel,
            stageCss);
    }

    private static IEnumerable<ErpPipelineItem> Sort(IEnumerable<ErpPipelineItem> items, string column, string direction)
    {
        Func<ErpPipelineItem, object?> keySelector = column.ToLowerInvariant() switch
        {
            "customer" => i => i.Transformed.CustomerName,
            "worktype" => i => i.Transformed.WorkType,
            "status" => i => i.Transformed.Status,
            "amount" => i => i.Transformed.Amount ?? decimal.MinValue,
            "sourcedate" => i => i.Raw.RawSourceDate,
            "normaliseddate" => i => i.Transformed.SourceDateUtc ?? DateTime.MinValue,
            _ => i => i.Raw.SourceErpId
        };

        return direction == "desc"
            ? items.OrderByDescending(keySelector)
            : items.OrderBy(keySelector);
    }

    private static ErpRecordRowViewModel ToRow(ErpPipelineItem item)
    {
        var transformed = item.Transformed;
        var (statusLabel, statusCss) = StatusPresentation.For(transformed.Status);
        var (stageLabel, stageCss) = StatusPresentation.For(item.Stage);

        return new ErpRecordRowViewModel(
            item.Raw.SourceErpId,
            transformed.CustomerName,
            transformed.WorkType,
            item.Raw.RawStatus ?? "—",
            statusLabel,
            statusCss,
            FormatAmount(transformed.Amount),
            item.Raw.RawSourceDate ?? "—",
            FormatUtcDate(transformed.SourceDateUtc),
            BuildValidationSummary(item.Validation),
            stageLabel,
            stageCss);
    }

    private static ValidationSummaryViewModel BuildValidationSummary(ValidationResult validation)
    {
        var messages = validation.Messages.Select(m => m.Message).ToList();

        if (validation.HasErrors)
        {
            var count = validation.Messages.Count(m => m.Severity == ValidationSeverity.Error);
            return new ValidationSummaryViewModel($"{count} error{(count == 1 ? "" : "s")}", "chip-validation--error", messages);
        }

        if (validation.HasWarnings)
        {
            var count = validation.Messages.Count(m => m.Severity == ValidationSeverity.Warning);
            return new ValidationSummaryViewModel($"{count} warning{(count == 1 ? "" : "s")}", "chip-validation--warning", messages);
        }

        return new ValidationSummaryViewModel("Valid", "chip-validation--ok", messages);
    }

    private static List<ActivityFeedItemViewModel> BuildRecentActivity(IReadOnlyList<ErpPipelineItem> items)
    {
        var now = DateTime.UtcNow;

        var activity = items.Select((item, index) =>
        {
            var message = item.Stage switch
            {
                ProcessingStage.Exception =>
                    $"{item.Raw.SourceErpId} flagged for review — {item.Validation.Messages.FirstOrDefault(m => m.Severity == ValidationSeverity.Error)?.Message ?? "validation error"}",
                ProcessingStage.Validating =>
                    $"{item.Raw.SourceErpId} transformed with warnings — {item.Validation.Messages.FirstOrDefault(m => m.Severity == ValidationSeverity.Warning)?.Message ?? "see details"}",
                ProcessingStage.New =>
                    $"{item.Raw.SourceErpId} received — awaiting source status",
                _ => $"{item.Raw.SourceErpId} transformed successfully"
            };

            var (_, cssClass) = StatusPresentation.For(item.Stage);
            var timestamp = now - TimeSpan.FromMinutes((items.Count - index) * 6);

            return (Timestamp: timestamp, Item: new ActivityFeedItemViewModel(
                item.Raw.SourceErpId,
                message,
                cssClass,
                $"{timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC"));
        });

        return activity.OrderByDescending(a => a.Timestamp).Select(a => a.Item).Take(8).ToList();
    }

    private static List<FilterOptionViewModel> BuildStatusFilterOptions(string? selected) =>
        new List<(string Value, string Label)>
        {
            ("", "All statuses"),
            (nameof(NormalisedStatus.New), "New"),
            (nameof(NormalisedStatus.InProgress), "In Progress"),
            (nameof(NormalisedStatus.Complete), "Complete"),
            (nameof(NormalisedStatus.Exception), "Exception")
        }
        .Select(o => new FilterOptionViewModel(o.Value, o.Label, string.Equals(o.Value, selected, StringComparison.OrdinalIgnoreCase) || (o.Value == "" && string.IsNullOrWhiteSpace(selected))))
        .ToList();

    private static List<FilterOptionViewModel> BuildValidationFilterOptions(string? selected) =>
        new List<(string Value, string Label)>
        {
            ("", "All results"),
            ("valid", "Valid"),
            ("warning", "Warning"),
            ("error", "Error")
        }
        .Select(o => new FilterOptionViewModel(o.Value, o.Label, string.Equals(o.Value, selected, StringComparison.OrdinalIgnoreCase) || (o.Value == "" && string.IsNullOrWhiteSpace(selected))))
        .ToList();

    private static readonly CultureInfo GbpCulture = CultureInfo.GetCultureInfo("en-GB");

    // "C" with en-GB gives "£1,250.00" / "-£150.00" — using the culture's own
    // negative-currency pattern instead of string-concatenating a "$" prefix
    // also fixes the previous bug where negative amounts rendered as "$-150.00".
    private static string FormatAmount(decimal? amount) =>
        amount.HasValue ? amount.Value.ToString("C", GbpCulture) : "—";

    private static string FormatUtcDate(DateTime? utc) =>
        utc.HasValue ? $"{utc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC" : "—";
}
