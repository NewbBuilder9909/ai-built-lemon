namespace ProgrammePulse.Models.Integrations.AzureDevOps.Raw;

/// <summary>
/// Bronze shapes for Azure DevOps (Services, dev.azure.com) — the only
/// place its API vocabulary appears. Everything above
/// AzureDevOpsEvidenceMapper sees Models/SkillsEvidence types instead.
///
/// Like the GitHub DTOs these carry no diff, patch or file content, and
/// have nowhere to put one.
///
/// Two differences from GitHub that shape the mapper:
///
/// 1. A **commit** carries only the git name and email — Azure DevOps does
///    not link a commit to an account id the way GitHub does. So a commit
///    actor has no <see cref="AzureDevOpsIdentity.Id"/> and can only reach
///    the mapping queue keyed by email, exactly like a co-author trailer.
/// 2. A **pull request** carries its reviewers inline, each with a vote,
///    rather than a separate review list. A reviewer with no vote was
///    asked, not heard from, and a container reviewer is a team or group,
///    not a person — the mapper drops both.
/// </summary>
public sealed record AzureDevOpsIdentity(
    string? Id,
    string? DisplayName,
    string? UniqueName,
    string? Email,
    bool IsContainer = false);

/// <summary>A repository the token can read. <paramref name="Key"/> is "project/repository".</summary>
public sealed record AzureDevOpsRepository(
    string Id,
    string ProjectName,
    string Name,
    string? DefaultBranch,
    bool IsDisabled)
{
    public string Key => $"{ProjectName}/{Name}";
}

/// <summary>A default-branch commit. <paramref name="Author"/> wrote it; <paramref name="Committer"/> applied it.</summary>
public sealed record AzureDevOpsCommit(
    string CommitId,
    string? Comment,
    DateTime? AuthoredAtUtc,
    DateTime? CommittedAtUtc,
    AzureDevOpsIdentity? Author,
    AzureDevOpsIdentity? Committer,
    string? WebUrl);

/// <summary>
/// A reviewer's position on a pull request. Azure DevOps' own scale:
/// 10 approved, 5 approved with suggestions, 0 no vote, -5 waiting for
/// author, -10 rejected.
/// </summary>
public sealed record AzureDevOpsReviewer(
    AzureDevOpsIdentity Identity,
    int Vote);

/// <summary>A completed (merged) pull request.</summary>
public sealed record AzureDevOpsPullRequest(
    int PullRequestId,
    string? Title,
    DateTime? ClosedAtUtc,
    AzureDevOpsIdentity? CreatedBy,
    IReadOnlyList<AzureDevOpsReviewer> Reviewers,
    string? WebUrl);

/// <summary>
/// One page of results, its resume cursor, and whether it was complete —
/// the same contract as GitHubPage, restated here so the two vendors never
/// reference each other. Null cursor with <paramref name="IsComplete"/>
/// false means the run stopped early, and the caller must record partial
/// coverage rather than "nothing more".
/// </summary>
public sealed record AzureDevOpsPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool IsComplete,
    string? IncompleteReason = null);
