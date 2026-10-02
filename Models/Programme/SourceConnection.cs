namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A tenant's own anchor for one external source (ClickUp or Hub Planner) —
/// originally the structural-only half of the GTM review's B03 item, now
/// also the tenant's own credential isolation: a tenant that sets its own
/// ClickUp token/workspace or Hub Planner key here (via
/// Controllers/StaffSourceConnectionController, /staffops/programme/connections)
/// syncs its own account instead of the deployment-wide
/// ClickUp:ApiToken / HubPlanner:ApiKey; a tenant that never configures one
/// keeps using the deployment-wide fallback with zero migration friction.
/// See <see cref="ProtectedCredentialJson"/> and
/// docs/programme-ops.md's "Tenant-owned source connections" section, and
/// docs/tenancy.md.
///
/// Auto-provisioned by the sync services on a tenant's first ClickUp/Hub
/// Planner sync (Services/ProgrammeOps/SourceConnectionRepository.
/// GetOrCreateAsync).
/// </summary>
public sealed record SourceConnection
{
    public required Guid ConnectionKey { get; init; }

    public required Guid TenantId { get; init; }

    public required string Source { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The source's own account identifier — a ClickUp workspace id or a Hub Planner account reference.</summary>
    public string? ExternalAccountId { get; init; }

    public required bool IsActive { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Opaque — produced and consumed only by ISourceCredentialProtector.
    /// Null means this tenant has not configured its own credential and the
    /// deployment-wide fallback (ClickUp:ApiToken / HubPlanner:ApiKey) is
    /// used instead.
    /// </summary>
    public string? ProtectedCredentialJson { get; init; }
}
