using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Durable sync-run state plus the per-(tenant, source) database lease.
/// Consumers: Services/Integrations/Resilience/SyncRunCoordinator (acquire,
/// heartbeat, complete/fail, release) and SyncStatusQueryService (freshness
/// for the Programme Overview header and the platform console).
///
/// The lease/run key is (tenantId, source), not source alone — otherwise
/// one tenant's ClickUp sync would serialize against every other tenant's
/// (see docs/tenancy.md). Ownership within that key is fenced on
/// (instanceId, runKey): a heartbeat or a completion from a worker that no
/// longer owns the lease affects zero rows and reports <c>false</c>, so a
/// worker that stalled past its lease and was taken over cannot keep
/// renewing, cannot publish "Succeeded", and cannot overwrite the
/// abandoned-run marker the new owner wrote. Lease expiry is judged on the
/// database clock (SYSUTCDATETIME), not on each application node's clock,
/// so clock skew between nodes cannot cause an early takeover.
/// </summary>
public interface ISyncRunRepository
{
    /// <summary>
    /// Atomically takes the (tenant, source) lease if nobody holds it or the
    /// holder's lease has expired, marks any still-Running run whose lease
    /// lapsed as Failed ("abandoned"), and inserts a new Running run.
    /// Returns null when another live owner holds the lease.
    /// </summary>
    Task<SyncRun?> TryAcquireAsync(Guid tenantId, string source, string instanceId, int? triggeredByMemberId, DateTime nowUtc, TimeSpan leaseDuration);

    /// <summary>
    /// Extends the lease and records the current stage. Returns false —
    /// and changes nothing — when this instance/run no longer owns the
    /// lease (another instance took it over after it expired).
    /// </summary>
    Task<bool> HeartbeatAsync(Guid runKey, Guid tenantId, string source, string instanceId, string? stage, DateTime nowUtc, TimeSpan leaseDuration);

    /// <summary>
    /// Marks the run Succeeded, but only while this instance/run still
    /// owns the lease and the run is still Running. Returns false without
    /// writing when either check fails, so a stale worker can never
    /// publish a success over a run the new owner already marked abandoned.
    /// </summary>
    Task<bool> CompleteAsync(Guid runKey, Guid tenantId, string source, string instanceId, string summary, string summaryJson, DateTime nowUtc);

    /// <summary>Marks the run Failed. Only a still-Running row transitions, so an abandoned marker is never overwritten.</summary>
    Task FailAsync(Guid runKey, string? stage, string error, DateTime nowUtc);

    /// <summary>Clears the lease if this instance and run own it.</summary>
    Task ReleaseAsync(Guid tenantId, string source, string instanceId, Guid runKey);

    Task<SyncRun?> GetLatestAsync(Guid tenantId, string source);

    Task<SyncRun?> GetLatestSuccessfulAsync(Guid tenantId, string source);

    Task<SyncRun?> GetRunningAsync(Guid tenantId, string source);

    /// <summary>
    /// The one deliberate cross-tenant read: the platform console's support
    /// signals show the latest run for a source across every tenant, not
    /// scoped to whoever is viewing (they're a Platform Admin, not a tenant
    /// member). Never call this from a tenant-facing page.
    /// </summary>
    Task<SyncRun?> GetLatestAcrossTenantsAsync(string source);

    /// <summary>Cross-tenant counterpart to <see cref="GetLatestSuccessfulAsync"/> — see <see cref="GetLatestAcrossTenantsAsync"/>.</summary>
    Task<SyncRun?> GetLatestSuccessfulAcrossTenantsAsync(string source);

    /// <summary>Retention purge of finished runs; returns rows deleted.</summary>
    Task<int> DeleteFinishedOlderThanAsync(DateTime cutoffUtc);
}
