using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ViewModels.Reporting;

/// <summary>
/// The Risk/Issue register — the RAID data has existed in Silver since
/// Programme Ops Phase 2 but nothing populated or rendered it until now.
/// </summary>
public sealed record RaidViewModel(
    IReadOnlyList<RiskRowViewModel> Risks,
    IReadOnlyList<IssueRowViewModel> Issues,
    IReadOnlyList<ProjectOptionViewModel> ProjectOptions)
{
    public ProgrammePulse.Models.ViewModels.ProgrammeOverview.PortfolioScopeViewModel Scope { get; init; }
        = ProgrammePulse.Models.ViewModels.ProgrammeOverview.PortfolioScopeViewModel.All;
}

public sealed record RiskRowViewModel(
    Guid RiskKey,
    string ProjectName,
    string Title,
    string? Description,
    SeverityLevel Severity,
    RiskStatus Status);

public sealed record IssueRowViewModel(
    Guid IssueKey,
    string ProjectName,
    string Title,
    string? Description,
    SeverityLevel Severity,
    IssueStatus Status);

public sealed record ProjectOptionViewModel(Guid ProjectKey, string Name);
