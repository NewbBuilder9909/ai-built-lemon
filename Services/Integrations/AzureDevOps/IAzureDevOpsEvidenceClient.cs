using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>What a connect-time probe learned about the token and organisation.</summary>
public enum AzureDevOpsVerification
{
    /// <summary>The token reads this organisation.</summary>
    Valid = 0,

    /// <summary>
    /// The token was refused — expired, revoked, lacking Code (Read), or
    /// issued for a different organisation. Azure DevOps reports this as a
    /// 401, a 203 sign-in page or a redirect depending on the route.
    /// </summary>
    TokenRejected = 1,

    /// <summary>No such organisation, or none this token's owner belongs to.</summary>
    OrganisationNotFound = 2
}

/// <summary>
/// Read-only Azure DevOps Services access for evidence ingestion. Bronze:
/// the only layer that knows the Azure DevOps REST shape.
///
/// Every method takes the connection's stored base URL and re-validates it
/// with AzureDevOpsHostPolicy; an implementation must not accept a host
/// from anywhere else. Pagination follows the same resumable contract as
/// the GitHub client: the caller stores the returned cursor only after the
/// page's contents are persisted, so a crash re-fetches rather than skips.
/// </summary>
public interface IAzureDevOpsEvidenceClient
{
    Task<AzureDevOpsVerification> VerifyOrganisationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken);

    /// <summary>Enabled repositories with a default branch, across every project the token can read.</summary>
    Task<IReadOnlyList<AzureDevOpsRepository>> GetAccessibleRepositoriesAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken);

    Task<AzureDevOpsPage<AzureDevOpsCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken);

    /// <summary>Completed pull requests closed since the cursor, reviewers inline.</summary>
    Task<AzureDevOpsPage<AzureDevOpsPullRequest>> GetCompletedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken);
}

/// <summary>
/// The connection can no longer read something it previously could — the
/// repository was deleted, disabled or moved out of the token owner's
/// reach, or the token itself stopped working. Coverage is marked
/// <c>PermissionLost</c>, not <c>Partial</c>: retrying will not help, and
/// the admin needs telling.
/// </summary>
public sealed class AzureDevOpsAccessLostException(string repositoryKey, string reason)
    : InvalidOperationException($"The Azure DevOps connection can no longer read '{repositoryKey}': {reason}.")
{
    public string RepositoryKey { get; } = repositoryKey;
}
