using ProgrammePulse.Models.ViewModels;

namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>
/// Gold-layer output — the only shape the Programme Overview page ever
/// renders. Deliberately carries no cost/rate fields: this page is visible
/// to Team Lead and above, not Admin-only, so nothing admin-only may ever
/// reach it. See Controllers/StaffProgrammeOverviewController.
///
/// Two capacity views are kept apart on purpose: ResourceCapacity is an
/// estimate-based workload indicator over open work items;
/// PlannedAllocations is scheduled time from a booking source. Neither is
/// a dated forecast, and neither feeds "done" / "% complete".
/// </summary>
public sealed record ProgrammeOverviewViewModel(
    IReadOnlyList<KpiCardViewModel> KpiCards,
    IReadOnlyList<WorkstreamStatusViewModel> WorkstreamStatuses,
    IReadOnlyList<MilestoneStatusViewModel> Milestones,
    IReadOnlyList<ResourceCapacityViewModel> ResourceCapacity,
    IReadOnlyList<PlannedAllocationSummaryViewModel> PlannedAllocations,
    PlannedAllocationCoverageViewModel PlannedCoverage,
    DateTime GeneratedAtUtc)
{
    public PortfolioScopeViewModel Scope { get; init; } = PortfolioScopeViewModel.All;
    public IReadOnlyList<ProgrammeHealthRow> ProgrammeHealth { get; init; } = [];

    /// <summary>Overdue open items by how late they are, split by whether anyone is named against them.</summary>
    public IReadOnlyList<OverdueAgeBandViewModel> OverdueByAge { get; init; } = [];

    /// <summary>
    /// Past their due date but with a status that can't be read, so not
    /// counted as overdue. Said on the page rather than dropped silently.
    /// </summary>
    public int PastDueWithUnreadableStatus { get; init; }
}

/// <summary>
/// One band of days late. Bands are fixed, so a quiet week and a bad week
/// are drawn on the same scale.
/// </summary>
public sealed record OverdueAgeBandViewModel(string Label, int FromDays, int? ToDays, int WithOwner = 0, int WithoutOwner = 0)
{
    public static IReadOnlyList<OverdueAgeBandViewModel> Bands { get; } =
    [
        new("1 to 7 days", 1, 7),
        new("8 to 30 days", 8, 30),
        new("31 days or more", 31, null)
    ];

    public int Total => WithOwner + WithoutOwner;

    public bool Contains(int daysLate) => daysLate >= FromDays && (ToDays is null || daysLate <= ToDays);
}

/// <summary>The page model: the Gold overview plus the management-header state around it.</summary>
public sealed record ProgrammeOverviewPageViewModel(
    ProgrammeOverviewViewModel Overview,
    IReadOnlyList<SourcePublicationStateViewModel> Sources,
    int UnresolvedIdentityCount,
    bool CanSync,
    IReadOnlyList<SyncSourceOptionViewModel> SyncSources,
    string? SyncMessage,
    string? SyncError);

/// <summary>
/// One sync button. Built from ISyncSourceRegistry, so the page renders a
/// button per registered-and-entitled source instead of hard-coding one per
/// vendor — adding a source adds a button with no view change.
/// </summary>
public sealed record SyncSourceOptionViewModel(string Source, string DisplayName);

public sealed record WorkstreamStatusViewModel(
    string ProgrammeName,
    string ProjectName,
    string WorkstreamName,
    int TotalItems,
    int DoneItems,
    int BlockedItems,
    int OverdueItems,
    int PercentComplete);

public sealed record MilestoneStatusViewModel(
    string Title,
    string WorkstreamName,
    DateTime? DueDateUtc,
    string StageLabel,
    bool IsOverdue,
    bool IsPastDueWithUnreadableStatus = false,
    int DaysPastDue = 0,
    bool IsClosed = false);

/// <summary>
/// OpenItemsWithoutEstimate is the coverage figure: how many of this
/// person's open items contributed nothing to EstimatedHoursOutstanding
/// because no estimate exists — missing is not zero.
/// </summary>
public sealed record ResourceCapacityViewModel(
    string StaffFullName,
    decimal CapacityHoursPerWeek,
    int AssignedOpenItems,
    decimal EstimatedHoursOutstanding,
    int OpenItemsWithoutEstimate);

/// <summary>
/// Planned (booked) time per person inside the reporting window. BookedHours
/// is null when none of the allocations carry hours from the source;
/// AllocationsWithoutHours says how many were excluded from that figure.
/// </summary>
public sealed record PlannedAllocationSummaryViewModel(
    string StaffFullName,
    int AllocationCount,
    decimal? BookedHours,
    int AllocationsWithoutHours,
    decimal CapacityHoursPerWeek = 0m,
    decimal? BookedHoursPerWeek = null)
{
    /// <summary>
    /// Booked beyond their contracted week on average across the window.
    /// Planned time, not delivery: says someone is over-committed, never
    /// that work is done or late.
    /// </summary>
    public bool IsBookedOverCapacity =>
        CapacityHoursPerWeek > 0 && BookedHoursPerWeek is { } perWeek && perWeek > CapacityHoursPerWeek;
}

public sealed record PlannedAllocationCoverageViewModel(
    int TotalInWindow,
    int WithoutResolvedStaff,
    int WithoutHours,
    int Undated,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc);
