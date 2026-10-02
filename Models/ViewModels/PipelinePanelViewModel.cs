namespace ProgrammePulse.Models.ViewModels;

public sealed record PipelineStageViewModel(string Label, string CssClass, int Count, int PercentOfTotal);

public sealed record PipelinePanelViewModel(IReadOnlyList<PipelineStageViewModel> Stages);
