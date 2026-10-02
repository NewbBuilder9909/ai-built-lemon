using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Persistence for the tenant-owned source connection registry (see
/// Models/Programme/SourceConnection). A dedicated repository rather than
/// folded into IProgrammeRepository — it is a genuinely separate aggregate
/// (no Programme/Project/WorkItem shape), same reasoning that already kept
/// identity resolution and sync-run state in their own repositories.
/// </summary>
public interface ISourceConnectionRepository
{
    Task<SourceConnection?> GetActiveForTenantAsync(Guid tenantId, string source);

    /// <summary>
    /// Returns the tenant's active connection for this source, creating one
    /// (auto-provisioned, DisplayName defaulted from the source name) if
    /// none exists yet — the "first sync creates the connection" behaviour
    /// described on SourceConnection's doc comment.
    /// </summary>
    Task<SourceConnection> GetOrCreateActiveAsync(Guid tenantId, string source, string? externalAccountId, DateTime nowUtc);

    /// <summary>
    /// Sets (or clears, when protectedCredentialJson is null) the tenant's
    /// own encrypted credential for this source — auto-provisioning the
    /// connection row first if none exists yet, same as
    /// GetOrCreateActiveAsync. Write-only from the caller's perspective:
    /// nothing in this repository decrypts the value it stores.
    /// </summary>
    Task<SourceConnection> SetCredentialAsync(Guid tenantId, string source, string? protectedCredentialJson, DateTime nowUtc);
}
