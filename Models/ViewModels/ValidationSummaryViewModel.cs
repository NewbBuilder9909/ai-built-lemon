namespace ProgrammePulse.Models.ViewModels;

public sealed record ValidationSummaryViewModel(string Label, string CssClass, IReadOnlyList<string> Messages);
