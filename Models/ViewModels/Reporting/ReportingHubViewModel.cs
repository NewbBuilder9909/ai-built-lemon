using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Models.ViewModels.Reporting;

/// <summary>
/// Gold-layer output for /staffops/reporting. Carries no cost/rate fields —
/// same principle as ProgrammeOverviewViewModel. Cost lives only in
/// CostSummaryViewModel, built by a separate admin-gated action/query.
/// </summary>
public sealed record ReportingHubViewModel(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? TeamFilter,
    IReadOnlyList<KpiCardViewModel> KpiCards,
    IReadOnlyList<EffortVarianceRowViewModel> EffortVariance,
    IReadOnlyList<ContributorCapacityRowViewModel> ContributorCapacity,
    IReadOnlyList<TeamCapacitySubtotalRowViewModel> TeamCapacitySubtotals,
    IReadOnlyList<CustomerRollupRowViewModel> CustomerRollup,
    IReadOnlyList<UnlinkedTimeEntryViewModel> UnlinkedTime,
    decimal UnknownBillabilityHours,
    DateTime GeneratedAtUtc,
    bool HasTempoDeletionCoverageGap = false,
    decimal UndatedTimeEntryHours = 0m,
    IReadOnlyList<string>? ExcludedOversightStaff = null)
{
    public PortfolioScopeViewModel Scope { get; init; } = PortfolioScopeViewModel.All;
}

/// <summary>Recorded effort that cannot be attributed to a known work item in the selected period.</summary>
public sealed record UnlinkedTimeEntryViewModel(
    string Source,
    string ExternalId,
    DateOnly WorkDate,
    decimal DurationHours,
    bool HasResolvedPerson);

/// <summary>
/// BaselineHours/BaselineVarianceHours are null until a Team Lead or Admin
/// explicitly locks a baseline for the workstream — EstimatedHours/
/// VarianceHours (current-estimate-based) stay populated either way so
/// existing callers/tests are unaffected. ForecastAtCompletionHours/
/// ForecastVarianceHours remain null until approved remaining-effort evidence
/// is supported. Task counts do not measure earned value.
/// </summary>
public sealed record EffortVarianceRowViewModel(
    Guid WorkstreamKey,
    string ProgrammeName,
    string ProjectName,
    string WorkstreamName,
    decimal EstimatedHours,
    decimal ActualHours,
    decimal VarianceHours,
    decimal? BaselineHours,
    decimal? BaselineVarianceHours,
    decimal? ForecastAtCompletionHours,
    decimal? ForecastVarianceHours,
    int WorkItemCount = 0,
    int WorkItemsWithoutEstimate = 0)
{
    public int EstimateCoveragePercent => WorkItemCount == 0
        ? 0
        : (int)Math.Round((WorkItemCount - WorkItemsWithoutEstimate) * 100m / WorkItemCount);
}

/// <summary>
/// UtilisationPercent is logged ÷ available, all hours blended regardless of
/// TimeEntry.IsBillable. BillableUtilisationPercent is the same calculation
/// restricted to billable entries only — the two diverge for anyone doing
/// substantial internal/non-chargeable work, which the blended figure alone
/// can't distinguish from someone fully utilised on chargeable work.
/// </summary>
public sealed record ContributorCapacityRowViewModel(
    Guid StaffKey,
    string StaffFullName,
    string? Team,
    decimal BaselineHours,
    decimal LeaveHours,
    decimal LoggedHours,
    decimal ResidualCapacityHours,
    int UtilisationPercent,
    int BillableUtilisationPercent);

/// <summary>
/// One row per distinct Team represented in ContributorCapacity for the same
/// scope/period — a Team Lead no longer has to mentally sum the per-person
/// rows to see whether their team as a whole is over- or under-committed.
/// "(No team)" groups any contributor with a null StaffProfile.Team.
/// </summary>
public sealed record TeamCapacitySubtotalRowViewModel(
    string TeamName,
    int ContributorCount,
    decimal BaselineHours,
    decimal LeaveHours,
    decimal LoggedHours,
    decimal ResidualCapacityHours,
    int UtilisationPercent,
    int BillableUtilisationPercent);

public sealed record CustomerRollupRowViewModel(
    string CustomerName,
    int ProgrammeCount,
    int OpenWorkItems,
    int BlockedWorkItems);

/// <summary>
/// Admin-only — built solely by ReportingQueryService.BuildCostSummaryAsync,
/// which is only ever called from StaffReportingController's IsAdminAsync()-
/// gated "cost" action. UnpricedEntryHours surfaces entries that couldn't be
/// costed (no StartedAtUtc, or no StaffRate effective at that date) so the
/// total's incompleteness is visible rather than silently under-reported.
/// GrandTotalCost sums TotalCost across every row regardless of currency —
/// only meaningful when TotalsByCurrency has exactly one entry. When staff
/// rates span more than one currency, use TotalsByCurrency instead; summing
/// GrandTotalCost across currencies would silently add incompatible units.
/// </summary>
public sealed record CostSummaryViewModel(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<CostSummaryRowViewModel> Rows,
    decimal GrandTotalCost,
    decimal UnpricedEntryHours,
    IReadOnlyList<CostSummaryCurrencyTotalViewModel> TotalsByCurrency,
    decimal UnknownBillabilityHours = 0m,
    bool HasTempoDeletionCoverageGap = false,
    decimal UndatedTimeEntryHours = 0m,
    IReadOnlyList<string>? ExcludedOversightStaff = null);

public sealed record CostSummaryRowViewModel(
    string StaffFullName,
    decimal LoggedHours,
    decimal BillableHours,
    decimal TotalCost,
    string Currency);

public sealed record CostSummaryCurrencyTotalViewModel(
    string Currency,
    decimal TotalCost);

/// <summary>
/// Change control + stakeholder register for /staffops/reporting/governance —
/// grouped onto one page the same way RaidViewModel groups risks and issues.
/// </summary>
public sealed record GovernanceViewModel(
    IReadOnlyList<ChangeRequestRowViewModel> ChangeRequests,
    IReadOnlyList<StakeholderRowViewModel> Stakeholders,
    IReadOnlyList<DependencyRowViewModel> Dependencies,
    IReadOnlyList<ProjectOptionViewModel> ProjectOptions,
    IReadOnlyList<ProgrammeOptionViewModel> ProgrammeOptions,
    IReadOnlyList<ProgrammePulse.Models.ViewModels.ProgrammeOverview.StaffOptionViewModel>? StaffOptions = null)
{
    public PortfolioScopeViewModel Scope { get; init; } = PortfolioScopeViewModel.All;
}

/// <summary>
/// A read-only rendering of one Dependency edge — "WorkItem is blocked by
/// DependsOnWorkItem" — not a graph, per docs/programme-ops.md's framing of
/// this as presentation over data that already exists. IsUnresolved is true
/// when DependsOnWorkItemStage isn't Done/Cancelled yet, i.e. this edge is
/// still actually blocking something today (the simplest usable "is this on
/// the critical path" signal this data model supports without a real
/// project schedule).
/// </summary>
public sealed record DependencyRowViewModel(
    Guid DependencyKey,
    string WorkstreamName,
    string WorkItemTitle,
    ProgrammePulse.Models.Programme.WorkItemLifecycleStage WorkItemStage,
    string DependsOnWorkItemTitle,
    ProgrammePulse.Models.Programme.WorkItemLifecycleStage DependsOnWorkItemStage,
    bool IsUnresolved);

public sealed record ChangeRequestRowViewModel(
    Guid ChangeRequestKey,
    string ProjectName,
    string Title,
    string? Description,
    ProgrammePulse.Models.Programme.ChangeRequestStatus Status,
    string? RequestedByName,
    string? DecidedByName,
    DateTime? DecidedAtUtc);

public sealed record StakeholderRowViewModel(
    Guid ProgrammeStakeholderKey,
    string ProgrammeName,
    string DisplayName,
    ProgrammePulse.Models.Programme.StakeholderRole Role);

public sealed record ProgrammeOptionViewModel(Guid ProgrammeKey, string Name);
