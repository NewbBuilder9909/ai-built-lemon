using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;
using ProgrammePulse.Services.Integrations.AzureDevOps;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// A scripted Azure DevOps organisation. The same role as
/// FakeGitHubEvidenceClient: partial pages, throttling and lost access are
/// data here, because none of them can be produced on demand against a
/// real organisation. A fixture replay is not a live vendor validation.
/// </summary>
public sealed class FakeAzureDevOpsEvidenceClient : IAzureDevOpsEvidenceClient
{
    public readonly Dictionary<string, Queue<AzureDevOpsPage<AzureDevOpsCommit>>> CommitPages = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, Queue<AzureDevOpsPage<AzureDevOpsPullRequest>>> PullRequestPages = new(StringComparer.OrdinalIgnoreCase);

    public readonly HashSet<string> AccessLost = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<string?> ObservedCommitCursors = [];

    public int Calls { get; private set; }

    public AzureDevOpsVerification Verification { get; set; } = AzureDevOpsVerification.Valid;

    public List<AzureDevOpsRepository> Accessible { get; set; } = [];

    public Task<AzureDevOpsVerification> VerifyOrganisationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Verification);
    }

    public Task<IReadOnlyList<AzureDevOpsRepository>> GetAccessibleRepositoriesAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<AzureDevOpsRepository>>(Accessible);
    }

    public Task<AzureDevOpsPage<AzureDevOpsCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        Calls++;
        ObservedCommitCursors.Add(cursor);
        ThrowIfAccessLost(repositoryKey);
        return Task.FromResult(Next(CommitPages, repositoryKey, new AzureDevOpsPage<AzureDevOpsCommit>([], cursor, true)));
    }

    public Task<AzureDevOpsPage<AzureDevOpsPullRequest>> GetCompletedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        Calls++;
        ThrowIfAccessLost(repositoryKey);
        return Task.FromResult(Next(PullRequestPages, repositoryKey, new AzureDevOpsPage<AzureDevOpsPullRequest>([], cursor, true)));
    }

    private void ThrowIfAccessLost(string repositoryKey)
    {
        if (AccessLost.Contains(repositoryKey))
        {
            throw new AzureDevOpsAccessLostException(repositoryKey, "the repository was not found or is no longer visible");
        }
    }

    private static AzureDevOpsPage<T> Next<T>(Dictionary<string, Queue<AzureDevOpsPage<T>>> pages, string repositoryKey, AzureDevOpsPage<T> empty) =>
        pages.TryGetValue(repositoryKey, out var queue) && queue.Count > 0 ? queue.Dequeue() : empty;

    // ---- fixture builders ----

    public FakeAzureDevOpsEvidenceClient WithCommits(string repositoryKey, AzureDevOpsPage<AzureDevOpsCommit> page)
    {
        if (!CommitPages.TryGetValue(repositoryKey, out var queue))
        {
            CommitPages[repositoryKey] = queue = new Queue<AzureDevOpsPage<AzureDevOpsCommit>>();
        }

        queue.Enqueue(page);
        return this;
    }

    public FakeAzureDevOpsEvidenceClient WithPullRequests(string repositoryKey, AzureDevOpsPage<AzureDevOpsPullRequest> page)
    {
        if (!PullRequestPages.TryGetValue(repositoryKey, out var queue))
        {
            PullRequestPages[repositoryKey] = queue = new Queue<AzureDevOpsPage<AzureDevOpsPullRequest>>();
        }

        queue.Enqueue(page);
        return this;
    }

    /// <summary>A git identity: what an Azure DevOps commit actually carries — a name and an email, no account.</summary>
    public static AzureDevOpsIdentity Git(string email, string? name = null) =>
        new(Id: null, DisplayName: name ?? email, UniqueName: null, Email: email);

    /// <summary>An account identity, as a pull request's author or reviewer.</summary>
    public static AzureDevOpsIdentity Account(string id, string uniqueName, string? displayName = null, bool isContainer = false) =>
        new(id, displayName ?? uniqueName, uniqueName, uniqueName.Contains('@') ? uniqueName : null, isContainer);

    public static AzureDevOpsIdentity BuildService(string organisation = "acme") =>
        new("build-1", $"Project Collection Build Service ({organisation})", "Build\\8a6f1c2e", null);

    public static AzureDevOpsCommit Commit(
        string commitId, AzureDevOpsIdentity? author, AzureDevOpsIdentity? committer = null, string? comment = null,
        DateTime? authoredAtUtc = null) =>
        new(commitId, comment ?? $"Change {commitId}",
            authoredAtUtc ?? new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            authoredAtUtc ?? new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            author, committer ?? author,
            $"https://dev.azure.com/acme/Web/_git/portal/commit/{commitId}");

    public static AzureDevOpsPullRequest PullRequest(
        int id, AzureDevOpsIdentity? createdBy, params AzureDevOpsReviewer[] reviewers) =>
        new(id, $"PR {id}", new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), createdBy, reviewers,
            $"https://dev.azure.com/acme/Web/_git/portal/pullrequest/{id}");
}
