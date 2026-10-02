namespace ProgrammePulse.Models.Integrations.GitHub.Raw;

/// <summary>
/// Bronze shapes — the only place GitHub's API vocabulary appears
/// alongside the fields this application actually needs. Everything above
/// the mapper sees Models/SkillsEvidence types instead, which is what
/// EvidenceProviderNeutralityTests enforces.
///
/// These carry no patch, diff or file content. The REST responses do
/// include them for some endpoints; the client never asks for those
/// fields, and the DTOs have nowhere to put them if it did.
/// </summary>
public sealed record GitHubActor(
    string? Id,
    string? Login,
    string? Type,
    string? Name,
    string? Email);

/// <summary>
/// A default-branch commit. <paramref name="Author"/> is who it names as the
/// writer; <paramref name="Committer"/> is who applied it.
/// <paramref name="SignatureVerified"/> is GitHub's <c>verification.verified</c>:
/// the commit is signed by a key GitHub ties to the committer's account. It
/// proves who signed, not who is named as author (a signer can name anyone),
/// and is null when GitHub did not say. <paramref name="SignatureReason"/> is
/// GitHub's reason code, e.g. <c>valid</c> or <c>unsigned</c>.
/// </summary>
public sealed record GitHubCommit(
    string Sha,
    string? Message,
    DateTime? AuthoredAtUtc,
    GitHubActor? Author,
    GitHubActor? Committer,
    string? HtmlUrl,
    IReadOnlyList<string> ChangedPaths,
    bool? SignatureVerified = null,
    string? SignatureReason = null);

public sealed record GitHubPullRequest(
    string Id,
    int Number,
    string? Title,
    DateTime? MergedAtUtc,
    GitHubActor? Author,
    string? HtmlUrl,
    IReadOnlyList<string> ChangedPaths);

public sealed record GitHubReview(
    string Id,
    string PullRequestId,
    int PullRequestNumber,
    string? State,
    DateTime? SubmittedAtUtc,
    GitHubActor? Reviewer,
    string? HtmlUrl);

/// <summary>
/// One page of results plus how to get the next one, and — importantly —
/// whether the page was complete.
///
/// <paramref name="NextCursor"/> null with <paramref name="IsComplete"/>
/// true means the stream finished. Null with false means it stopped early
/// (rate limit, failed page, cancellation), which the caller must record
/// as partial coverage rather than as "there is nothing more".
/// </summary>
public sealed record GitHubPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool IsComplete,
    string? IncompleteReason = null);
