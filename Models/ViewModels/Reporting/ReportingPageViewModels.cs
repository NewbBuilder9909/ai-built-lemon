using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ViewModels.Reporting;

/// <summary>
/// Everything the Reporting Hub page renders. The Gold read model plus the
/// page's own inputs used to travel as five untyped ViewData entries, which
/// neither the compiler nor the Razor check could see.
/// </summary>
public sealed record ReportingHubPageViewModel(
    ReportingHubViewModel Hub,
    bool CanViewCost,
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<Programme.Programme> Programmes,
    Guid? ProgrammeFilter,
    Guid? CustomerFilter,
    int OpenAlertCount);

/// <summary>The Cost Summary page: the cost read model plus the programme budget table.</summary>
public sealed record CostPageViewModel(
    CostSummaryViewModel Summary,
    IReadOnlyList<ProgrammeBudgetRowViewModel> ProgrammeBudgets);
