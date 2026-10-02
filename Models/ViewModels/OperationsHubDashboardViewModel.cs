namespace ProgrammePulse.Models.ViewModels;

public sealed record OperationsHubDashboardViewModel(
    IReadOnlyList<KpiCardViewModel> KpiCards,
    StatusSummaryViewModel StatusSummary,
    PipelinePanelViewModel Pipeline,
    IReadOnlyList<ActivityFeedItemViewModel> RecentActivity);
