using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Services.Integrations.GitHub;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// A scripted GitHub. Every scenario Slice 2 has to handle — a partial
/// page, a rate limit, a revoked permission, a force-push that rewrites a
/// SHA, a squash merge — is expressible here as data, which is the whole
/// point: none of them can be produced on demand against a real
/// repository, and a connector that has only been tested on the happy
/// path is a connector whose failure modes are unknown.
///
/// A fixture replay is not a live vendor validation, and nothing in this
/// file should be read as one. The application labels evidence built this
/// way as an unverified replay until a real installation has completed a
/// clean run — see EvidenceCoverageSummary.IsUnverifiedReplay.
/// </summary>
public sealed class FakeGitHubEvidenceClient : IGitHubEvidenceClient
{
    public Action? DuringCommitFetch { get; set; }
    /// <summary>Per repository, the commit pages a run will see, in order.</summary>
    public readonly Dictionary<string, Queue<GitHubPage<GitHubCommit>>> CommitPages = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, Queue<GitHubPage<GitHubPullRequest>>> PullRequestPages = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, Queue<GitHubPage<GitHubReview>>> ReviewPages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Repositories the connection can no longer read.</summary>
    public readonly HashSet<string> AccessLost = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<string?> ObservedCommitCursors = [];

    public string? GrantedAccount { get; set; } = "acme-ltd";

    public List<string> AccessibleRepositories { get; set; } = ["acme-ltd/web"];

    public Task<string?> VerifyInstallationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(GrantedAccount);

    public Task<IReadOnlyList<string>> GetAccessibleRepositoriesAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(AccessibleRepositories);

    public Task<GitHubPage<GitHubCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        ObservedCommitCursors.Add(cursor);
        DuringCommitFetch?.Invoke();
        ThrowIfAccessLost(repositoryKey);
        return Task.FromResult(Next(CommitPages, repositoryKey, new GitHubPage<GitHubCommit>([], cursor, true)));
    }

    public Task<GitHubPage<GitHubPullRequest>> GetMergedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        ThrowIfAccessLost(repositoryKey);
        return Task.FromResult(Next(PullRequestPages, repositoryKey, new GitHubPage<GitHubPullRequest>([], cursor, true)));
    }

    public Task<GitHubPage<GitHubReview>> GetReviewsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, IReadOnlyList<int> pullRequestNumbers, CancellationToken cancellationToken)
    {
        ThrowIfAccessLost(repositoryKey);
        return Task.FromResult(Next(ReviewPages, repositoryKey, new GitHubPage<GitHubReview>([], null, true)));
    }

    private void ThrowIfAccessLost(string repositoryKey)
    {
        if (AccessLost.Contains(repositoryKey))
        {
            throw new GitHubAccessLostException(repositoryKey);
        }
    }

    /// <summary>
    /// Pages are consumed in order; once the queue is empty the same
    /// "nothing new" answer repeats, so a second run against an
    /// unchanged repository behaves like the real thing.
    /// </summary>
    private static GitHubPage<T> Next<T>(Dictionary<string, Queue<GitHubPage<T>>> pages, string repositoryKey, GitHubPage<T> empty) =>
        pages.TryGetValue(repositoryKey, out var queue) && queue.Count > 0 ? queue.Dequeue() : empty;

    // ---- fixture builders ----

    public FakeGitHubEvidenceClient WithCommits(string repositoryKey, GitHubPage<GitHubCommit> page)
    {
        if (!CommitPages.TryGetValue(repositoryKey, out var queue))
        {
            CommitPages[repositoryKey] = queue = new Queue<GitHubPage<GitHubCommit>>();
        }

        queue.Enqueue(page);
        return this;
    }

    public FakeGitHubEvidenceClient WithPullRequests(string repositoryKey, GitHubPage<GitHubPullRequest> page)
    {
        if (!PullRequestPages.TryGetValue(repositoryKey, out var queue))
        {
            PullRequestPages[repositoryKey] = queue = new Queue<GitHubPage<GitHubPullRequest>>();
        }

        queue.Enqueue(page);
        return this;
    }

    public FakeGitHubEvidenceClient WithReviews(string repositoryKey, GitHubPage<GitHubReview> page)
    {
        if (!ReviewPages.TryGetValue(repositoryKey, out var queue))
        {
            ReviewPages[repositoryKey] = queue = new Queue<GitHubPage<GitHubReview>>();
        }

        queue.Enqueue(page);
        return this;
    }

    public static GitHubActor Human(string id, string login, string? email = null) =>
        new(id, login, "User", login, email);

    public static GitHubActor Bot(string id, string login) => new(id, login, "Bot", login, null);

    /// <summary>A trailer-only co-author: no account, an email and nothing else.</summary>
    public static GitHubActor TrailerOnly(string email, string? name = null) =>
        new(Id: null, Login: null, Type: null, Name: name, Email: email);

    public static GitHubCommit Commit(
        string sha, GitHubActor? author, GitHubActor? committer = null, string? message = null,
        DateTime? authoredAtUtc = null, params string[] paths) =>
        new(sha, message ?? $"Change {sha}", authoredAtUtc ?? new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            author, committer, $"https://github.com/acme-ltd/web/commit/{sha}", paths);

    public static GitHubPullRequest PullRequest(
        string id, int number, GitHubActor? author, DateTime? mergedAtUtc = null, params string[] paths) =>
        new(id, number, $"PR {number}", mergedAtUtc ?? new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc),
            author, $"https://github.com/acme-ltd/web/pull/{number}", paths);

    public static GitHubReview Review(string id, int pullRequestNumber, GitHubActor? reviewer, string state = "APPROVED") =>
        new(id, pullRequestNumber.ToString(), pullRequestNumber, state,
            new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc), reviewer,
            $"https://github.com/acme-ltd/web/pull/{pullRequestNumber}#review-{id}");
}
