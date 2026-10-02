namespace ProgrammePulse.Models.ViewModels.SkillsEvidence;

/// <summary>
/// The repository picker for one Azure DevOps evidence connection. The
/// list is what the stored token can read right now, fetched live — so
/// an admin can only tick a repository the grant actually covers.
/// </summary>
public sealed record AzureDevOpsRepositorySelectionViewModel(
    Guid ConnectionKey,
    string Organisation,
    IReadOnlyList<AzureDevOpsRepositoryOption> Repositories,
    int MaxSelectable);

/// <param name="Key">"project/repository".</param>
public sealed record AzureDevOpsRepositoryOption(string Key, string Project, string Name, bool Selected);
