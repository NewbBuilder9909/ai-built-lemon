namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>Explicit URL scope. Tenant membership is resolved before this model is built.</summary>
public sealed record PortfolioScopeViewModel(
    Guid? ProgrammeKey, Guid? CustomerKey, string Label, bool IsValid,
    IReadOnlyList<PortfolioScopeOption> Programmes, IReadOnlyList<PortfolioScopeOption> Customers)
{
    public static PortfolioScopeViewModel All { get; } = new(null, null, "All programmes / all customers", true, [], []);
}

public sealed record PortfolioScopeOption(Guid Key, string Name);

/// <summary>Observed exceptions, not an assurance rating or a financial forecast.</summary>
public sealed record ProgrammeHealthRow(
    Guid ProgrammeKey, string ProgrammeName, string CustomerName,
    int TotalItems, int DoneItems, int CancelledItems, int BlockedItems, int OverdueItems,
    int OpenRisks, int HighRisks, int OpenIssues, int ProposedChanges,
    int UnresolvedDependencies, int MissingEstimates, int UnmappedStatuses,
    int WorkstreamsWithoutBaseline, string AccountableOwners,
    string? NextMilestone, DateTime? NextMilestoneDueUtc, int UndatedMilestones,
    string ReviewSignal, string ReviewReasons);
