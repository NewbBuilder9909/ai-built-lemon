using System.Text.RegularExpressions;

namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// Which API hosts an evidence connection may talk to, and what a valid
/// repository selection looks like.
///
/// Pure and unit-tested on its own, for the same reason as
/// Startup/ProductionConfigurationGuard: this is the check that stops a
/// tenant admin — or anyone who can reach the connect form — pointing the
/// connector at a host of their choosing and having the application send
/// it credentials. "Reject arbitrary API hosts" is a Slice 2 requirement,
/// and a requirement enforced inside an HTTP client is a requirement
/// nobody can review.
///
/// The allow-list is deliberately short. github.com's API, and GitHub
/// Enterprise Server hosts that a deployment operator has explicitly
/// configured — never a host a request can introduce.
/// </summary>
public static partial class EvidenceHostPolicy
{
    public const string GitHubDotComApi = "https://api.github.com";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex RepositorySegment();

    /// <summary>
    /// Validates a candidate API base URL against github.com plus any
    /// operator-configured Enterprise hosts, and returns it in canonical
    /// form. Null means reject — callers must not fall back to a default.
    /// </summary>
    /// <param name="allowedEnterpriseHosts">
    /// Hosts from deployment configuration only. An empty list means
    /// github.com is the only option, which is the safe default.
    /// </param>
    public static string? Canonicalize(string? candidate, IEnumerable<string>? allowedEnterpriseHosts = null)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        // HTTPS only, no credentials in the URL, no query or fragment, and
        // a default port — each of these has been an exfiltration trick.
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.IsDefaultPort)
        {
            return null;
        }

        if (string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            // No path is permitted on github.com's API root.
            return uri.AbsolutePath is "/" or "" ? GitHubDotComApi : null;
        }

        var enterprise = allowedEnterpriseHosts?.ToArray() ?? [];
        if (enterprise.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            // GitHub Enterprise Server serves its API under /api/v3.
            var path = uri.AbsolutePath.TrimEnd('/');
            return path is "" or "/api/v3" ? $"https://{uri.Host.ToLowerInvariant()}/api/v3" : null;
        }

        return null;
    }

    /// <summary>
    /// "owner/name", each segment a plausible repository path component.
    /// Rejects traversal, absolute paths, wildcards and anything that would
    /// change the shape of a request URL.
    /// </summary>
    public static bool IsValidRepositoryKey(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var parts = candidate.Trim().Split('/');
        return parts.Length == 2
            && RepositorySegment().IsMatch(parts[0])
            && RepositorySegment().IsMatch(parts[1])
            && parts.All(part => part is not ("." or ".."));
    }

    /// <summary>
    /// A selection is valid only if every entry parses and belongs to the
    /// account the connection was granted on. A repository from another
    /// organisation in the list would mean fetching data the tenant's grant
    /// does not cover.
    /// </summary>
    public static bool IsValidSelection(IEnumerable<string>? repositories, string sourceAccountId, out IReadOnlyList<string> normalized)
    {
        var accepted = new List<string>();
        normalized = accepted;

        if (repositories is null || string.IsNullOrWhiteSpace(sourceAccountId))
        {
            return false;
        }

        foreach (var repository in repositories)
        {
            var trimmed = repository?.Trim();
            if (!IsValidRepositoryKey(trimmed))
            {
                return false;
            }

            var owner = trimmed!.Split('/')[0];
            if (!string.Equals(owner, sourceAccountId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var canonical = trimmed.ToLowerInvariant();
            if (!accepted.Contains(canonical, StringComparer.Ordinal))
            {
                accepted.Add(canonical);
            }
        }

        return accepted.Count is > 0 and <= 200;
    }
}

/// <summary>
/// Whether an actor is a bot or service account. Bots are excluded from
/// person attribution entirely — a dependency-update bot that opens four
/// hundred pull requests would otherwise dominate every contribution
/// figure in the product.
///
/// Excluded, not dropped: bot sightings still reach the queue marked
/// <see cref="UnmappedActorReason.Bot"/>, so an admin can see that the
/// exclusion happened and correct it if a real person's account has been
/// caught by the name rules.
/// </summary>
public static class EvidenceActorClassifier
{
    /// <summary>The provider's own account type value for an app/bot account.</summary>
    public const string BotAccountType = "Bot";

    private static readonly string[] BotLoginSuffixes = ["[bot]", "-bot", "_bot", "-robot"];

    private static readonly string[] KnownBotLogins =
    [
        "dependabot", "renovate", "github-actions", "greenkeeper", "snyk-bot",
        "codecov", "mergify", "imgbot", "allcontributors", "semantic-release-bot",
        "web-flow"
    ];

    /// <summary>
    /// <paramref name="accountType"/> is the provider's own classification
    /// and is trusted first — it is the only signal that is actually
    /// authoritative. The name rules are a fallback for trailers and
    /// plain-git commits, which carry no account type at all.
    /// </summary>
    public static bool IsBot(string? login, string? accountType = null, string? email = null)
    {
        if (string.Equals(accountType, BotAccountType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = login?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(name))
        {
            if (KnownBotLogins.Contains(name, StringComparer.Ordinal)
                || BotLoginSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        // GitHub's web UI attributes browser-made commits to this address.
        return email is not null
            && email.Trim().EndsWith("@users.noreply.github.com", StringComparison.OrdinalIgnoreCase)
            && email.Contains("web-flow", StringComparison.OrdinalIgnoreCase);
    }
}
