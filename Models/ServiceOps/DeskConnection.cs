using System.Text.RegularExpressions;

namespace ProgrammePulse.Models.ServiceOps;

public enum DeskConnectionStatus
{
    Active = 0,
    Disconnected = 1,

    /// <summary>The credential no longer works, or lost the scope it needs. Nobody here chose it.</summary>
    AccessLost = 2
}

/// <summary>
/// One tenant's read-only connection to one support desk account.
///
/// Mirrors <c>SkillsEvidence_Connection</c> in shape and in its
/// guarantees — several accounts per tenant, credential encrypted under
/// its own purpose, **no deployment-wide fallback** — but is a separate
/// table in a separate feature area on purpose. The design document
/// promises that a customer can connect a desk without connecting a
/// repository; making service health depend on the engineering-evidence
/// area's table would quietly break that.
/// </summary>
public sealed record DeskConnection
{
    public required Guid ConnectionKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>Stable provider name, e.g. "Freshdesk".</summary>
    public required string Provider { get; init; }

    /// <summary>The desk account — for Freshdesk, the subdomain. Unique per (tenant, provider).</summary>
    public required string SourceAccountId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Validated at write time against <see cref="DeskHostPolicy"/>. Never taken from a request unchecked.</summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>
    /// The component/product tags this tenant has agreed to report
    /// against. A case tagged with anything else is counted as having an
    /// unmapped component and shown as such — rather than the product
    /// inventing a category from whatever the desk happened to contain.
    /// </summary>
    public IReadOnlyList<string> ApprovedComponents { get; init; } = [];

    public required DeskConnectionStatus Status { get; init; }

    /// <summary>Opaque; produced and consumed only by IDeskCredentialProtector.</summary>
    public string? ProtectedCredentialJson { get; init; }

    public Guid? ConnectedByStaffKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public DateTime? DisconnectedAtUtc { get; init; }

    public bool IsUsable => Status == DeskConnectionStatus.Active && ProtectedCredentialJson is not null;
}

/// <summary>The credential for one desk connection. Separate from every other protector, and with no fallback.</summary>
public sealed record DeskCredential(string ApiToken, string? AccountDomain = null);

/// <summary>
/// Which desk API hosts a connection may talk to.
///
/// Same reasoning as EvidenceHostPolicy, and the same reason it is pure
/// and unit-tested: a host check buried inside an HTTP client is a check
/// nobody can review, and this is what stops a tenant admin — or anyone
/// who reaches the connect form — pointing the connector at a host of
/// their choosing and having the application send it a credential.
///
/// Freshdesk is per-customer subdomain, so unlike GitHub there is no
/// single fixed host. The rule is therefore structural: a valid
/// subdomain, then the vendor's own domain suffix, and nothing else.
/// </summary>
public static partial class DeskHostPolicy
{
    public const string FreshdeskProvider = "Freshdesk";

    private const string FreshdeskSuffix = ".freshdesk.com";

    /// <summary>Letters, digits and hyphens, not starting or ending with one — the vendor's own subdomain rule.</summary>
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex Subdomain();

    /// <summary>
    /// Validates a Freshdesk account domain and returns the canonical API
    /// base URL, or null to reject. Callers must not fall back to a
    /// default on null.
    ///
    /// Accepts either the bare subdomain ("acme") or the full host
    /// ("acme.freshdesk.com"), because an admin will reasonably type
    /// either — but never an arbitrary URL.
    /// </summary>
    public static string? CanonicalizeFreshdesk(string? accountOrHost)
    {
        if (string.IsNullOrWhiteSpace(accountOrHost))
        {
            return null;
        }

        var value = accountOrHost.Trim().ToLowerInvariant();

        // Tolerate a pasted URL, but only by extracting the host from it —
        // scheme, port, path, query and credentials are all discarded
        // rather than honoured.
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !uri.IsDefaultPort)
            {
                return null;
            }

            value = uri.Host;
        }

        if (value.EndsWith(FreshdeskSuffix, StringComparison.Ordinal))
        {
            value = value[..^FreshdeskSuffix.Length];
        }

        return Subdomain().IsMatch(value) ? $"https://{value}{FreshdeskSuffix}/api/v2" : null;
    }

    /// <summary>The account identifier stored on the connection — the bare subdomain.</summary>
    public static string? AccountIdFor(string provider, string? accountOrHost)
    {
        if (!string.Equals(provider, FreshdeskProvider, StringComparison.Ordinal))
        {
            return null;
        }

        var canonical = CanonicalizeFreshdesk(accountOrHost);
        if (canonical is null)
        {
            return null;
        }

        var host = new Uri(canonical).Host;
        return host[..^FreshdeskSuffix.Length];
    }

    /// <summary>
    /// A component tag the tenant may approve. Kept deliberately narrow:
    /// these become axis labels on a chart and row keys in a report, and
    /// a desk's free-text tag field will contain anything at all.
    /// </summary>
    public static bool IsValidComponentKey(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && candidate.Trim().Length <= 64
        && candidate.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ' or '/');

    /// <summary>Folds a desk's raw tag to the canonical form used for matching against the approved list.</summary>
    public static string NormalizeComponent(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
}
