namespace ProgrammePulse.Models.ViewModels;

public sealed record StatusSummaryItemViewModel(string Label, string CssClass, int Count, int PercentOfTotal);

public sealed record StatusSummaryViewModel(IReadOnlyList<StatusSummaryItemViewModel> Items);
