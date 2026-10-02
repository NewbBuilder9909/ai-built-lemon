namespace ProgrammePulse.Models.ViewModels.Reporting;

/// <summary>
/// Admin-only, same gating as CostSummaryViewModel — built by
/// IProgrammeBudgetService, which reads IStaffRateRepository the same way
/// ReportingQueryService.BuildCostSummaryAsync does. BudgetAmount/
/// BudgetCurrency null means no budget has been set yet, not zero;
/// BurnPercent is null until both a budget and a currency exist to compute
/// it against.
/// </summary>
public sealed record ProgrammeBudgetRowViewModel(
    Guid ProgrammeKey,
    string ProgrammeName,
    string? CustomerName,
    decimal? BudgetAmount,
    string? BudgetCurrency,
    decimal CostToDate,
    decimal? BurnPercent,
    decimal UnpricedHours,
    decimal MismatchedCurrencyHours);
