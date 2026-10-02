using System.Text.RegularExpressions;
using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>
/// The seam where Azure DevOps Bronze DTOs and the provider-neutral
/// <see cref="EngineeringEvidence"/> contract both appear. Same job as
/// GitHubEvidenceMapper — one row per person per role — and deliberately
/// independent of it: the two vendors never reference each other, so the
/// few lines of git-trailer handling are restated here rather than shared.
///
/// What differs from GitHub, and why:
///
/// - **Commit actors have no account id.** Azure DevOps gives a commit's
///   git name and email only, so commit rows are keyed by email and reach
///   a person only after an admin approves that email on the queue. The
///   same person's pull-request identity (a stable account id) is a
///   separate queue row. Two approvals for one person is the price of
///   never guessing that an email and an account are the same human.
/// - **Reviews come from pull-request votes.** A reviewer with vote 0 was
///   asked, not heard from, and is skipped. A container reviewer is a team
///   or group, not a person, and is skipped. The vote is kept in the title
///   so a reader can tell an approval from a rejection.
/// - **Build identities are bots.** Pipeline and build-service accounts
///   are marked with the bot account type before the resolver sees them,
///   so they are excluded from attribution but still visible on the queue.
///
/// Pure apart from the resolver call: no HTTP, no database.
/// </summary>
public sealed partial class AzureDevOpsEvidenceMapper(IEvidenceActorResolver resolver)
{
    public const string ProviderName = AzureDevOpsHostPolicy.ProviderName;

    [GeneratedRegex(@"(?im)^\s*co-authored-by:\s*(?<name>[^<]*?)\s*<(?<email>[^>]+)>\s*$")]
    private static partial Regex CoAuthorTrailer();

    private static readonly string[] BotDisplayNames =
    [
        "azure pipelines", "microsoft.visualstudio.services.tfs", "project collection build service",
        "azure devops", "dependabot"
    ];

    public async Task<IReadOnlyList<EngineeringEvidence>> MapCommitAsync(
        AzureDevOpsCommit commit, EvidenceConnection connection, string repositoryKey, Guid? runKey, DateTime nowUtc)
    {
        var rows = new List<EngineeringEvidence>();
        var occurredAt = commit.AuthoredAtUtc ?? commit.CommittedAtUtc ?? nowUtc;
        var title = Title(commit.Comment);

        if (commit.Author is { } author)
        {
            rows.Add(await BuildAsync(author, EvidenceRole.CommitAuthor, EvidenceSourceType.Commit, commit.CommitId,
                connection, repositoryKey, title, commit.WebUrl, occurredAt, runKey, nowUtc));
        }

        // Applying a change is not writing it: a separate role, and only
        // when it is actually a different person.
        if (commit.Committer is { } committer && !SameGitIdentity(commit.Author, committer))
        {
            rows.Add(await BuildAsync(committer, EvidenceRole.CommitCommitter, EvidenceSourceType.Commit, commit.CommitId,
                connection, repositoryKey, title, commit.WebUrl, occurredAt, runKey, nowUtc));
        }

        foreach (var coAuthor in CoAuthors(commit.Comment))
        {
            rows.Add(await BuildAsync(coAuthor, EvidenceRole.CoAuthor, EvidenceSourceType.Commit, commit.CommitId,
                connection, repositoryKey, title, commit.WebUrl, occurredAt, runKey, nowUtc));
        }

        return rows;
    }

    public async Task<IReadOnlyList<EngineeringEvidence>> MapPullRequestAsync(
        AzureDevOpsPullRequest pullRequest, EvidenceConnection connection, string repositoryKey, Guid? runKey, DateTime nowUtc)
    {
        var rows = new List<EngineeringEvidence>();
        var occurredAt = pullRequest.ClosedAtUtc ?? nowUtc;
        var externalId = pullRequest.PullRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (pullRequest.CreatedBy is { IsContainer: false } author)
        {
            rows.Add(await BuildAsync(author, EvidenceRole.PullRequestAuthor, EvidenceSourceType.PullRequest, externalId,
                connection, repositoryKey, pullRequest.Title, pullRequest.WebUrl, occurredAt, runKey, nowUtc));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reviewer in pullRequest.Reviewers)
        {
            if (reviewer.Vote == 0 || reviewer.Identity.IsContainer)
            {
                continue;
            }

            // One review row per reviewer per pull request. The vote is the
            // reviewer's final position, so a later change updates the row.
            var reviewerKey = reviewer.Identity.Id ?? reviewer.Identity.UniqueName;
            if (reviewerKey is null || !seen.Add(reviewerKey))
            {
                continue;
            }

            rows.Add(await BuildAsync(reviewer.Identity, EvidenceRole.Reviewer, EvidenceSourceType.Review,
                $"{externalId}:{reviewerKey}", connection, repositoryKey,
                $"Review ({VoteLabel(reviewer.Vote)}) on !{externalId}", pullRequest.WebUrl, occurredAt, runKey, nowUtc));
        }

        return rows;
    }

    /// <summary>Azure DevOps' vote scale in words. Kept in the title rather than scored.</summary>
    public static string VoteLabel(int vote) => vote switch
    {
        >= 10 => "approved",
        > 0 => "approved with suggestions",
        <= -10 => "rejected",
        < 0 => "waiting for author",
        _ => "no vote"
    };

    /// <summary>
    /// Build and pipeline identities. Azure DevOps names them predictably
    /// ("Project Collection Build Service (acme)", "Web Build Service
    /// (acme)") and gives them a "Build\" unique name.
    /// </summary>
    public static bool IsServiceIdentity(AzureDevOpsIdentity identity)
    {
        var name = identity.DisplayName?.Trim().ToLowerInvariant() ?? string.Empty;
        var unique = identity.UniqueName?.Trim() ?? string.Empty;

        return name.Contains(" build service", StringComparison.Ordinal)
            || name.StartsWith("build service", StringComparison.Ordinal)
            || BotDisplayNames.Contains(name, StringComparer.Ordinal)
            || unique.StartsWith("Build\\", StringComparison.OrdinalIgnoreCase)
            || unique.StartsWith("vstfs:", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<EngineeringEvidence> BuildAsync(
        AzureDevOpsIdentity actor, EvidenceRole role, EvidenceSourceType sourceType, string externalId,
        EvidenceConnection connection, string repositoryKey, string? title, string? sourceUrl,
        DateTime occurredAtUtc, Guid? runKey, DateTime nowUtc)
    {
        var accountType = IsServiceIdentity(actor) ? EvidenceActorClassifier.BotAccountType : null;
        var resolution = await resolver.ResolveAsync(
            new EvidenceActorSighting(actor.Id, actor.UniqueName, actor.DisplayName, actor.Email, accountType),
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
            ActorExternalId = ActorId(actor),
            ActorLogin = actor.UniqueName ?? actor.DisplayName,
            ActorEmail = actor.Email,
            ActorIsBot = resolution.IsBot,
            StaffKey = resolution.StaffKey,
            AttributionStatus = resolution.Status,
            RepositoryKey = repositoryKey,
            Title = title,
            SourceUrl = sourceUrl,
            OccurredAtUtc = occurredAtUtc,

            // The list endpoints carry no changed paths, and fetching each
            // commit's changes is a per-commit fan-out this slice does not
            // make. "No hints" here means "not looked", not "no languages".
            LanguageHints = [],
            ObservedInRunKey = runKey,
            SchemaVersion = EngineeringEvidenceSchema.CurrentVersion,
            FirstIngestedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Must match EvidenceActorResolver's key exactly — the account id when
    /// there is one, otherwise the same email-then-login synthetic id — or
    /// approving a queue row would attribute nothing.
    /// </summary>
    private static string? ActorId(AzureDevOpsIdentity actor)
    {
        if (!string.IsNullOrWhiteSpace(actor.Id))
        {
            return actor.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return "email:" + actor.Email.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(actor.UniqueName) ? null : "login:" + actor.UniqueName.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<AzureDevOpsIdentity> CoAuthors(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return [];
        }

        var found = new List<AzureDevOpsIdentity>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in CoAuthorTrailer().Matches(comment))
        {
            var email = match.Groups["email"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            if (email.Length > 0 && seen.Add(email))
            {
                found.Add(new AzureDevOpsIdentity(Id: null, DisplayName: name.Length == 0 ? null : name, UniqueName: null, Email: email));
            }
        }

        return found;
    }

    /// <summary>Git identities have no id, so "same person" means the same email.</summary>
    private static bool SameGitIdentity(AzureDevOpsIdentity? left, AzureDevOpsIdentity? right) =>
        left is not null && right is not null
        && !string.IsNullOrWhiteSpace(left.Email)
        && string.Equals(left.Email.Trim(), right.Email?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Title(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        // Subject line only; a commit body can hold anything.
        var newline = comment.IndexOfAny(['\r', '\n']);
        return (newline < 0 ? comment : comment[..newline]).Trim();
    }
}
