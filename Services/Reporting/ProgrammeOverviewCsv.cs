using System.Globalization;
using System.Text;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.Reporting;

/// <summary>
/// The Programme Overview as a sectioned CSV — the same shape as the
/// Reporting Hub export — so the page every manager lands on can be shared
/// with someone who works in a spreadsheet.
///
/// Provenance comes first on purpose: the product's claim is evidence you can
/// challenge, and a figure that travels without its source, freshness and
/// unmatched-people count can't be challenged. Carries no cost or rate data,
/// like the page itself (see CLAUDE.md, "Cost/rate data isolation").
/// </summary>
public static class ProgrammeOverviewCsv
{
    public static string Build(ProgrammeOverviewPageViewModel page, string organisation, DateTime generatedAtUtc)
    {
        var overview = page.Overview;
        var csv = new StringBuilder();

        csv.Append(CsvWriter.WriteRow("Programme Overview"));
        csv.Append(CsvWriter.WriteRow("Organisation", organisation));
        csv.Append(CsvWriter.WriteRow("Delivery scope", overview.Scope.Label));
        csv.Append(CsvWriter.WriteRow("Programme key", overview.Scope.ProgrammeKey?.ToString() ?? "ALL", "Customer key", overview.Scope.CustomerKey?.ToString() ?? "ALL"));
        csv.Append(CsvWriter.WriteRow("Scope note", "Source freshness and unmatched-person counts are organisation-wide. Workflow done counts are not acceptance, earned value or financial performance."));
        csv.Append(CsvWriter.WriteRow("Generated (UTC)", generatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        csv.Append(CsvWriter.WriteRow("People in source data not matched to a staff profile", page.UnresolvedIdentityCount.ToString(CultureInfo.InvariantCulture)));

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Data sources"));
        csv.Append(CsvWriter.WriteRow("Source", "Status", "Last complete publication (UTC)", "Freshness", "Detail"));
        foreach (var source in page.Sources.Where(s => s.HasActivity))
        {
            csv.Append(CsvWriter.WriteRow(source.DisplayName, source.StatusLabel,
                source.LastPublishedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                source.FreshnessLabel,
                source.LastRunFailed ? source.LastFailureError : source.LastPublishedSummary));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Programme health review"));
        csv.Append(CsvWriter.WriteRow("Programme", "Customer", "Accountable", "Review signal", "Workflow done", "Total items", "Cancelled", "Blocked", "Overdue", "Open risks", "High/critical risks", "Open issues", "Proposed changes", "Unresolved dependencies", "Open items without estimates", "Unmapped states", "Workstreams without baseline", "Earliest outstanding milestone", "Milestone due (UTC)", "Undated milestones", "Reasons"));
        foreach (var row in overview.ProgrammeHealth)
        {
            csv.Append(CsvWriter.WriteRow(row.ProgrammeName, row.CustomerName, row.AccountableOwners, row.ReviewSignal,
                Number(row.DoneItems), Number(row.TotalItems), Number(row.CancelledItems), Number(row.BlockedItems), Number(row.OverdueItems),
                Number(row.OpenRisks), Number(row.HighRisks), Number(row.OpenIssues), Number(row.ProposedChanges), Number(row.UnresolvedDependencies),
                Number(row.MissingEstimates), Number(row.UnmappedStatuses), Number(row.WorkstreamsWithoutBaseline), row.NextMilestone,
                row.NextMilestoneDueUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Number(row.UndatedMilestones), row.ReviewReasons));
        }
        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Headline figures"));
        csv.Append(CsvWriter.WriteRow("Figure", "Value", "Out of"));
        foreach (var card in overview.KpiCards)
        {
            csv.Append(CsvWriter.WriteRow(card.Label, card.Value, card.Denominator is { } outOf ? Number(outOf) : null));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Workstream status"));
        csv.Append(CsvWriter.WriteRow("Programme", "Project", "Workstream", "Total", "Done", "Blocked", "Overdue", "% Complete"));
        foreach (var row in overview.WorkstreamStatuses)
        {
            csv.Append(CsvWriter.WriteRow(row.ProgrammeName, row.ProjectName, row.WorkstreamName,
                Number(row.TotalItems), Number(row.DoneItems), Number(row.BlockedItems), Number(row.OverdueItems), Number(row.PercentComplete)));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Milestones"));
        csv.Append(CsvWriter.WriteRow("Milestone", "Workstream", "Due", "Stage", "Overdue"));
        foreach (var milestone in overview.Milestones)
        {
            csv.Append(CsvWriter.WriteRow(milestone.Title, milestone.WorkstreamName,
                milestone.DueDateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                milestone.StageLabel, milestone.IsOverdue ? "Yes" : "No"));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Outstanding workload (estimate-based; not a dated forecast)"));
        csv.Append(CsvWriter.WriteRow("Staff", "Capacity (hrs/wk)", "Open items", "Estimated hours outstanding", "Items without estimate"));
        foreach (var row in overview.ResourceCapacity)
        {
            csv.Append(CsvWriter.WriteRow(row.StaffFullName, Hours(row.CapacityHoursPerWeek), Number(row.AssignedOpenItems),
                Hours(row.EstimatedHoursOutstanding), Number(row.OpenItemsWithoutEstimate)));
        }

        return csv.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hours(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
