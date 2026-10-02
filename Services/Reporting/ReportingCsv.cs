using System.Text;
using ProgrammePulse.Models.ViewModels.Reporting;

namespace ProgrammePulse.Services.Reporting;

/// <summary>
/// The Reporting Hub and Cost Summary CSV exports. Pure formatting of an
/// already-built read model, so the column layout is testable without HTTP.
/// Moved verbatim from StaffReportingController.
/// </summary>
public static class ReportingCsv
{
    public static byte[] Hub(ReportingHubViewModel model)
    {
        var csv = new StringBuilder();
        csv.Append(CsvWriter.WriteRow("Delivery scope", model.Scope.Label));
        csv.Append(CsvWriter.WriteRow("Programme key", model.Scope.ProgrammeKey?.ToString() ?? "ALL", "Customer key", model.Scope.CustomerKey?.ToString() ?? "ALL"));
        csv.Append(CsvWriter.WriteRow("Period", model.PeriodStart.ToString("yyyy-MM-dd"), model.PeriodEnd.ToString("yyyy-MM-dd")));
        csv.Append(CsvWriter.WriteRow("Scope note", "Delivery KPIs, effort variance and customer totals are scoped. Capacity remains team/organisation-wide across all programmes; unattributed-time coverage is organisation-wide. Effort variance is cumulative."));
        csv.Append(CsvWriter.WriteRow("Forecast evidence", "Unavailable until approved remaining-effort estimates exist. Workflow counts do not establish earned value."));
        csv.Append(CsvWriter.WriteRow("Effort Variance"));
        csv.Append(CsvWriter.WriteRow("Programme", "Project", "Workstream", "Estimated Hours", "Actual Hours", "Variance Hours", "Baseline Hours", "Baseline Variance Hours", "Forecast At Completion Hours", "Forecast Variance Hours"));
        foreach (var row in model.EffortVariance)
        {
            csv.Append(CsvWriter.WriteRow(row.ProgrammeName, row.ProjectName, row.WorkstreamName,
                row.EstimatedHours.ToString("0.##"), row.ActualHours.ToString("0.##"), row.VarianceHours.ToString("0.##"),
                row.BaselineHours?.ToString("0.##") ?? "", row.BaselineVarianceHours?.ToString("0.##") ?? "",
                row.ForecastAtCompletionHours?.ToString("0.##") ?? "", row.ForecastVarianceHours?.ToString("0.##") ?? ""));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Contributor Capacity"));
        csv.Append(CsvWriter.WriteRow("Staff", "Team", "Baseline Hours", "Leave Hours", "Logged Hours", "Residual Hours", "Utilisation %", "Billable Utilisation %"));
        foreach (var row in model.ContributorCapacity)
        {
            csv.Append(CsvWriter.WriteRow(row.StaffFullName, row.Team, row.BaselineHours.ToString("0.##"),
                row.LeaveHours.ToString("0.##"), row.LoggedHours.ToString("0.##"), row.ResidualCapacityHours.ToString("0.##"),
                row.UtilisationPercent.ToString(), row.BillableUtilisationPercent.ToString()));
        }

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Unknown billability hours", model.UnknownBillabilityHours.ToString("0.##")));
        csv.Append(CsvWriter.WriteRow("Undated recorded hours outside period totals", model.UndatedTimeEntryHours.ToString("0.##")));
        if (model.HasTempoDeletionCoverageGap)
            csv.Append(CsvWriter.WriteRow("Coverage warning", "Tempo historical deletion coverage is not verified; verify recorded hours in Tempo."));
        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Unlinked Recorded Time"));
        csv.Append(CsvWriter.WriteRow("Source", "Source Record ID", "Work Date", "Hours", "Person Mapped"));
        foreach (var row in model.UnlinkedTime)
            csv.Append(CsvWriter.WriteRow(row.Source, row.ExternalId, row.WorkDate.ToString("yyyy-MM-dd"),
                row.DurationHours.ToString("0.##"), row.HasResolvedPerson ? "Yes" : "No"));

        csv.Append(CsvWriter.WriteRow());
        csv.Append(CsvWriter.WriteRow("Portfolio by Customer"));
        csv.Append(CsvWriter.WriteRow("Customer", "Programmes", "Open Work Items", "Blocked"));
        foreach (var row in model.CustomerRollup)
        {
            csv.Append(CsvWriter.WriteRow(row.CustomerName, row.ProgrammeCount.ToString(), row.OpenWorkItems.ToString(), row.BlockedWorkItems.ToString()));
        }

        return CsvWriter.ToBytes(csv.ToString());
    }

    public static byte[] Cost(CostSummaryViewModel model)
    {
        var csv = new StringBuilder();
        csv.Append(CsvWriter.WriteRow("Staff", "Logged Hours", "Billable Hours", "Total Cost", "Currency"));
        foreach (var row in model.Rows)
        {
            csv.Append(CsvWriter.WriteRow(row.StaffFullName, row.LoggedHours.ToString("0.##"), row.BillableHours.ToString("0.##"),
                row.TotalCost.ToString("0.##"), row.Currency));
        }

        csv.Append(CsvWriter.WriteRow("Unknown billability hours", model.UnknownBillabilityHours.ToString("0.##")));
        csv.Append(CsvWriter.WriteRow("Undated recorded hours outside period totals", model.UndatedTimeEntryHours.ToString("0.##")));
        if (model.HasTempoDeletionCoverageGap)
            csv.Append(CsvWriter.WriteRow("Coverage warning", "Tempo historical deletion coverage is not verified; verify recorded hours in Tempo."));

        csv.Append(CsvWriter.WriteRow());
        if (model.TotalsByCurrency.Count > 1)
        {
            foreach (var total in model.TotalsByCurrency)
            {
                csv.Append(CsvWriter.WriteRow($"Total ({total.Currency})", "", "", total.TotalCost.ToString("0.##"), total.Currency));
            }
        }
        else
        {
            csv.Append(CsvWriter.WriteRow("Grand Total", "", "", model.GrandTotalCost.ToString("0.##"), model.TotalsByCurrency.FirstOrDefault()?.Currency ?? ""));
        }
        csv.Append(CsvWriter.WriteRow("Unpriced Entry Hours", model.UnpricedEntryHours.ToString("0.##")));

        return CsvWriter.ToBytes(csv.ToString());
    }
}
