using ProgrammePulse.Models.Integrations.GitHub.Raw;

namespace ProgrammePulse.Services.Integrations.GitHub;

/// <summary>
/// Read-only GitHub access for evidence ingestion. Bronze: the only layer
/// that knows GitHub's API shape.
///
/// Every method takes the validated API base URL from the connection
/// rather than resolving a host itself — see EvidenceHostPolicy. An
/// implementation must not accept a host from anywhere else.
///
/// Pagination is explicit and resumable. A caller passes the cursor it
/// stored last time and gets back the next one, which it stores **only**
/// after the page's contents are persisted. Re-fetching a page is free
/// because the evidence upsert is idempotent on its stable key; skipping
/// one loses data silently, so the design errs towards re-fetching.
/// </summary>
public interface IGitHubEvidenceClient
{
    /// <summary>
    /// Confirms the grant and returns the account login it covers, or null
    /// if the token cannot be used. Called at connect time so the admin
    /// sees which account they actually granted, rather than the one they
    /// think they did.
    /// </summary>
    Task<string?> VerifyInstallationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken);

    /// <summary>The repositories the installation can actually read — the truth the admin's selection is checked against.</summary>
    Task<IReadOnlyList<string>> GetAccessibleRepositoriesAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken);

    Task<GitHubPage<GitHubCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken);

    Task<GitHubPage<GitHubPullRequest>> GetMergedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken);

    Task<GitHubPage<GitHubReview>> GetReviewsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, IReadOnlyList<int> pullRequestNumbers, CancellationToken cancellationToken);
}

/// <summary>
/// The connection no longer has permission to read something it previously
/// could — the installation was removed, or a repository left its scope.
/// Distinct from a transient failure: coverage is marked
/// <c>PermissionLost</c> rather than <c>Partial</c>, because retrying will
/// not help and the admin needs telling.
/// </summary>
public sealed class GitHubAccessLostException(string repositoryKey)
    : InvalidOperationException($"The GitHub connection can no longer read '{repositoryKey}'.")
{
    public string RepositoryKey { get; } = repositoryKey;
}
