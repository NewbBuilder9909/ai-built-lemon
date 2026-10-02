namespace ProgrammePulse.Models.SkillsEvidence;

public enum EvidenceConnectionStatus
{
    /// <summary>Authorised and usable.</summary>
    Active = 0,

    /// <summary>The tenant admin disconnected it. Evidence is retained but marked out of coverage.</summary>
    Disconnected = 1,

    /// <summary>
    /// The grant no longer works — the installation was removed at the
    /// provider, or a repository left the selection. Distinct from
    /// <see cref="Disconnected"/> because nobody here chose it, and the
    /// admin needs telling.
    /// </summary>
    AccessLost = 2
}

/// <summary>
/// One tenant's authorised, read-only connection to one source account —
/// a GitHub App installation on one organisation, limited to the
/// repositories that organisation's admin selected.
///
/// **Why this is not `ProgrammeOps_SourceConnection`.** That table is the
/// delivery-data connection (ClickUp, Hub Planner, Jira, Tempo) and differs
/// from what this slice requires in two ways that matter. First, it is one
/// row per (tenant, source), so a tenant cannot connect two GitHub
/// organisations — and the design document calls out multiple accounts of
/// the same provider per tenant as a requirement. Second, ClickUp and Hub
/// Planner deliberately fall back to a deployment-wide credential when a
/// tenant has not configured its own; Slice 2 forbids that outright for
/// person-level evidence, and inheriting a table whose documented behaviour
/// is a fallback would be the wrong thing to build on. There is no
/// deployment-wide GitHub credential anywhere in this feature, and no code
/// path that could reach one.
///
/// Credentials never live on this record in the clear — see
/// <see cref="ProtectedCredentialJson"/>.
/// </summary>
public sealed record EvidenceConnection
{
    public required Guid ConnectionKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>Stable provider name, e.g. "GitHub".</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The provider's own account identifier — a GitHub organisation login.
    /// Unique per (tenant, provider) so one tenant can hold several
    /// accounts of the same provider, each with its own credential, cursor
    /// and identity links.
    /// </summary>
    public required string SourceAccountId { get; init; }

    /// <summary>What the admin sees: "github.com/acme-ltd".</summary>
    public required string DisplayName { get; init; }

    /// <summary>The provider's installation identifier, shown to the admin so they can verify the grant.</summary>
    public string? InstallationId { get; init; }

    /// <summary>
    /// The API host this connection talks to, validated at write time
    /// against an allow-list. Stored per connection rather than assumed, so
    /// a GitHub Enterprise host is explicit — and so an attacker-supplied
    /// host can never be reached. See EvidenceHostPolicy.
    /// </summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>
    /// The repositories the admin selected, "owner/name" each. Evidence is
    /// ingested for these and no others; a repository that disappears from
    /// this list stops being synced and its coverage goes stale rather than
    /// its evidence being deleted.
    /// </summary>
    public IReadOnlyList<string> SelectedRepositories { get; init; } = [];

    public required EvidenceConnectionStatus Status { get; init; }

    /// <summary>Opaque; produced and consumed only by IEvidenceCredentialProtector.</summary>
    public string? ProtectedCredentialJson { get; init; }

    public Guid? ConnectedByStaffKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public DateTime? DisconnectedAtUtc { get; init; }

    public bool IsUsable => Status == EvidenceConnectionStatus.Active && ProtectedCredentialJson is not null;
}

/// <summary>
/// The credential for one evidence connection. Separate from ProgrammeOps'
/// <c>SourceCredential</c> so the two protector purposes, and the two
/// fallback policies, cannot be confused: there is no fallback here.
/// </summary>
public sealed record EvidenceCredential(
    string AccessToken,
    DateTimeOffset? ExpiresAtUtc = null,
    string? RefreshToken = null,
    string? InstallationId = null);
