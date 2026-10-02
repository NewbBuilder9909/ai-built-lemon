using System.Text.RegularExpressions;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Services.Integrations.GitHub;

/// <summary>
/// The seam where GitHub Bronze DTOs and provider-neutral Silver records
/// both appear — the equivalent of ClickUpMappingService, and the only
/// type in this feature allowed to reference both.
///
/// Its whole job is turning artefacts into **one row per person per role**.
/// A merged pull request with an author, two reviewers and a commit whose
/// committer differs from its author produces five rows, not one, because
/// those are five different claims about four different people and
/// flattening them would lose exactly the distinctions the design
/// document insists on keeping.
///
/// Pure apart from the resolver call: no HTTP, no database. That is what
/// lets the force-push, squash, co-author and bot cases be tested as data
/// rather than against a live repository.
/// </summary>
public sealed partial class GitHubEvidenceMapper(IEvidenceActorResolver resolver)
{
    public const string ProviderName = "GitHub";

    /// <summary>
    /// <c>Co-authored-by: Name &lt;email&gt;</c>, as git and GitHub define
    /// it. Read from the trailer only — a co-author is never inferred from
    /// anything else, because guessing who was pairing is exactly the kind
    /// of attribution this product must not invent.
    /// </summary>
    [GeneratedRegex(@"(?im)^\s*co-authored-by:\s*(?<name>[^<]*?)\s*<(?<email>[^>]+)>\s*$")]
    private static partial Regex CoAuthorTrailer();

    public async Task<IReadOnlyList<EngineeringEvidence>> MapCommitAsync(
        GitHubCommit commit, EvidenceConnection connection, string repositoryKey, Guid? runKey, DateTime nowUtc)
    {
        var rows = new List<EngineeringEvidence>();
        var occurredAt = commit.AuthoredAtUtc ?? nowUtc;

        // A verified signature proves who signed, which is the committer.
        // The author field is free text the signer chose, so authorship is
        // verified only when the author is that same signer; a co-author
        // trailer never is (Aikido: insufficient verification of data
        // authenticity). Unverified rows are kept and labelled, not dropped.
        // Null when GitHub did not say, which is recorded as such rather than
        // as a failed check; both read as "authorship not verified".
        var signed = commit.SignatureVerified;

        // The author wrote it.
        if (commit.Author is { } author)
        {
            rows.Add(await BuildAsync(
                author, EvidenceRole.CommitAuthor, EvidenceSourceType.Commit, commit.Sha,
                connection, repositoryKey, Title(commit.Message), commit.HtmlUrl, occurredAt,
                ChangedPathClassifier.Hints(commit.ChangedPaths), runKey, nowUtc,
                authorshipVerified: signed is null ? null : signed.Value && SameActor(author, commit.Committer)));
        }

        // The committer applied it — frequently a rebase, a squash or a
        // merge-queue bot, which is why this is a separate role and never
        // counted as authorship.
        if (commit.Committer is { } committer && !SameActor(commit.Author, committer))
        {
            rows.Add(await BuildAsync(
                committer, EvidenceRole.CommitCommitter, EvidenceSourceType.Commit, commit.Sha,
                connection, repositoryKey, Title(commit.Message), commit.HtmlUrl, occurredAt,
                // No language hints on the committer row: applying a squash
                // is not participating in the files it contains.
                [], runKey, nowUtc, authorshipVerified: signed));
        }

        foreach (var coAuthor in CoAuthors(commit.Message))
        {
            rows.Add(await BuildAsync(
                coAuthor, EvidenceRole.CoAuthor, EvidenceSourceType.Commit, commit.Sha,
                connection, repositoryKey, Title(commit.Message), commit.HtmlUrl, occurredAt,
                ChangedPathClassifier.Hints(commit.ChangedPaths), runKey, nowUtc, authorshipVerified: false));
        }

        return rows;
    }

    public async Task<IReadOnlyList<EngineeringEvidence>> MapPullRequestAsync(
        GitHubPullRequest pullRequest, EvidenceConnection connection, string repositoryKey, Guid? runKey, DateTime nowUtc)
    {
        if (pullRequest.Author is null)
        {
            return [];
        }

        return
        [
            await BuildAsync(
                pullRequest.Author, EvidenceRole.PullRequestAuthor, EvidenceSourceType.PullRequest, pullRequest.Id,
                connection, repositoryKey, pullRequest.Title, pullRequest.HtmlUrl,
                pullRequest.MergedAtUtc ?? nowUtc,
                ChangedPathClassifier.Hints(pullRequest.ChangedPaths), runKey, nowUtc)
        ];
    }

    public async Task<IReadOnlyList<EngineeringEvidence>> MapReviewAsync(
        GitHubReview review, EvidenceConnection connection, string repositoryKey, Guid? runKey, DateTime nowUtc)
    {
        // A "COMMENTED" review with no verdict is weaker evidence than an
        // approval or a change request, and the state is kept in the title
        // so a reader can tell them apart rather than the mapper deciding
        // which ones count.
        if (review.Reviewer is null)
        {
            return [];
        }

        return
        [
            await BuildAsync(
                review.Reviewer, EvidenceRole.Reviewer, EvidenceSourceType.Review, review.Id,
                connection, repositoryKey,
                $"Review ({review.State ?? "unknown"}) on #{review.PullRequestNumber}", review.HtmlUrl,
                review.SubmittedAtUtc ?? nowUtc, [], runKey, nowUtc)
        ];
    }

    private async Task<EngineeringEvidence> BuildAsync(
        GitHubActor actor, EvidenceRole role, EvidenceSourceType sourceType, string externalId,
        EvidenceConnection connection, string repositoryKey, string? title, string? sourceUrl,
        DateTime occurredAtUtc, IReadOnlyList<string> languageHints, Guid? runKey, DateTime nowUtc,
        bool? authorshipVerified = null)
    {
        var resolution = await resolver.ResolveAsync(
            new EvidenceActorSighting(actor.Id, actor.Login, actor.Name, actor.Email, actor.Type),
            connection.ConnectionKey, ProviderName, connection.TenantId, nowUtc);

        return new EngineeringEvidence
        {
            EvidenceKey = Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Provider = ProviderName,
            SourceAccountId = connection.SourceAccountId,
            SourceType = sourceType,
            ExternalId = externalId,
            Role = role,
            ActorExternalId = actor.Id ?? SyntheticActorId(actor),
            ActorLogin = actor.Login,
            ActorEmail = actor.Email,
            ActorIsBot = resolution.IsBot,
            // Only ever what the resolver returned, which is only ever
            // backed by an approved link.
            StaffKey = resolution.StaffKey,
            AttributionStatus = resolution.Status,
            RepositoryKey = repositoryKey,
            Title = title,
            SourceUrl = sourceUrl,
            OccurredAtUtc = occurredAtUtc,
            LanguageHints = languageHints,
            AuthorshipVerified = authorshipVerified,
            ObservedInRunKey = runKey,
            SchemaVersion = EngineeringEvidenceSchema.CurrentVersion,
            FirstIngestedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Must match EvidenceActorResolver's synthetic id exactly, or a
    /// trailer-only co-author would be queued under one key and stored
    /// under another — and approving the queue row would attribute nothing.
    /// </summary>
    private static string? SyntheticActorId(GitHubActor actor)
    {
        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return "email:" + actor.Email.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(actor.Login) ? null : "login:" + actor.Login.Trim().ToLowerInvariant();
    }

    private IReadOnlyList<GitHubActor> CoAuthors(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var found = new List<GitHubActor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in CoAuthorTrailer().Matches(message))
        {
            var email = match.Groups["email"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            if (email.Length == 0 || !seen.Add(email))
            {
                continue;
            }

            // A trailer carries no account id and no account type, so the
            // actor is identified by email alone — which is precisely why
            // it can only ever reach the queue, never a profile.
            found.Add(new GitHubActor(Id: null, Login: null, Type: null, Name: name.Length == 0 ? null : name, Email: email));
        }

        return found;
    }

    /// <summary>
    /// Same person in both git roles — the ordinary case for a direct
    /// commit, where emitting a second row would double-count them.
    /// </summary>
    private static bool SameActor(GitHubActor? left, GitHubActor? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id))
        {
            return string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(left.Email)
            && string.Equals(left.Email, right.Email, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Title(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // The subject line only. A commit body can contain anything,
        // including text a contributor would not expect to be stored.
        var newline = message.IndexOfAny(['\r', '\n']);
        var subject = newline < 0 ? message : message[..newline];
        return subject.Trim();
    }
}
