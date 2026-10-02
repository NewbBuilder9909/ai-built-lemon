using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Models.ViewModels.ProgrammeOverview;

/// <summary>
/// /staffops/programme/repositories: which repositories serve which
/// projects, and which repositories the evidence sources read that no
/// project has claimed yet.
/// </summary>
public sealed record CodeRepositoriesViewModel(
    IReadOnlyList<CodeRepositoryLinkRowViewModel> Links,
    PageLinks LinkPages,
    IReadOnlyList<ProjectOptionViewModel> Projects,
    IReadOnlyList<CodeRepositoryRef> Unclaimed,
    bool EvidenceSourcesAvailable,
    string? Message);

public sealed record CodeRepositoryLinkRowViewModel(
    Guid LinkKey,
    CodeRepositoryRef Repository,
    string ProjectName,
    string ProgrammeName,
    string? CustomerName,
    string? Note,
    DateTime LinkedAtUtc);

public sealed record ProjectOptionViewModel(Guid ProjectKey, string Label);
