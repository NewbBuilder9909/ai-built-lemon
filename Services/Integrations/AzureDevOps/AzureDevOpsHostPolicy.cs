using System.Text.RegularExpressions;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>
/// Which Azure DevOps endpoint an evidence connection may talk to, and what
/// a valid organisation and repository selection look like. The Azure
/// DevOps counterpart of EvidenceHostPolicy, kept in this vendor's folder
/// so the shared policy does not grow a second provider's rules.
///
/// Only Azure DevOps Services is accepted: <c>https://dev.azure.com/{org}</c>.
/// The legacy <c>{org}.visualstudio.com</c> form and on-premises Azure
/// DevOps Server are refused rather than half-supported — the connection
/// form cannot introduce a host, so there is no request-supplied value that
/// could send a tenant's token anywhere else.
/// </summary>
public static partial class AzureDevOpsHostPolicy
{
    public const string ProviderName = "AzureDevOps";

    public const string ServiceRoot = "https://dev.azure.com";

    public const int MaxSelectedRepositories = 200;

    /// <summary>Organisation names: letters, digits and hyphens, not starting or ending with a hyphen.</summary>
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,48}[A-Za-z0-9])?$")]
    private static partial Regex OrganisationPattern();

    /// <summary>
    /// Project and repository names. Narrower than Azure DevOps allows on
    /// purpose: a name outside this set simply cannot be selected, which is
    /// a support conversation, whereas a permissive pattern is a URL-shape
    /// question nobody reviews.
    /// </summary>
    [GeneratedRegex(@"^[\p{L}\p{N}_][\p{L}\p{N} ._()-]{0,63}$")]
    private static partial Regex NamePattern();

    public static bool IsValidOrganisation(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && OrganisationPattern().IsMatch(candidate.Trim());

    public static bool IsValidName(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && NamePattern().IsMatch(candidate)
        && !candidate.EndsWith('.')
        && !candidate.EndsWith(' ')
        && !candidate.Contains("..", StringComparison.Ordinal);

    /// <summary>"project/repository", each segment a valid name.</summary>
    public static bool IsValidRepositoryKey(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var parts = candidate.Split('/');
        return parts.Length == 2 && IsValidName(parts[0]) && IsValidName(parts[1]);
    }

    /// <summary>The API base for an organisation, or null if the organisation name is not acceptable.</summary>
    public static string? BaseUrlFor(string? organisation) =>
        IsValidOrganisation(organisation) ? $"{ServiceRoot}/{organisation!.Trim().ToLowerInvariant()}" : null;

    /// <summary>
    /// Validates a stored base URL and returns it in canonical form, or null
    /// to reject. Called again by the client on every request, so a row
    /// edited in the database cannot redirect a token either.
    /// </summary>
    public static string? Canonicalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.IsDefaultPort
            || !string.Equals(uri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        return segments.Length == 1 ? BaseUrlFor(segments[0]) : null;
    }

    /// <summary>
    /// A selection is valid only if every entry is a well-formed key the
    /// token can actually read. Membership of <paramref name="accessible"/>
    /// is what stops an admin selecting a repository the grant does not
    /// cover; the canonical spelling is taken from that list, not the form.
    /// </summary>
    public static bool IsValidSelection(
        IEnumerable<string>? requested, IReadOnlyCollection<string> accessible, out IReadOnlyList<string> normalized)
    {
        var accepted = new List<string>();
        normalized = accepted;

        if (requested is null)
        {
            return false;
        }

        foreach (var entry in requested)
        {
            var trimmed = entry?.Trim();
            if (!IsValidRepositoryKey(trimmed))
            {
                return false;
            }

            var canonical = accessible.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                return false;
            }

            if (!accepted.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                accepted.Add(canonical);
            }
        }

        return accepted.Count is > 0 and <= MaxSelectedRepositories;
    }
}
