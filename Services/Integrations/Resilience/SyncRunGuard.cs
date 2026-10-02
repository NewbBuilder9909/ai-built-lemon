using System.Collections.Concurrent;

namespace ProgrammePulse.Services.Integrations.Resilience;

/// <summary>
/// Process-wide "one sync per (tenant, source) at a time" latch. Two Admins
/// of the same tenant clicking "Run sync now" within seconds of each other
/// would otherwise run two interleaved fetch→upsert passes over the same
/// (TenantId, ExternalSource, ExternalId) keys — the upsert's read-then-write
/// isn't atomic, so the loser can insert a duplicate Silver row. Registered
/// as a singleton. Keyed by tenant as well as source (not source alone) so
/// two different tenants syncing the same source never serialize against
/// each other in-process — this was the one residual gap the "tenant safe
/// pilot foundation" phase closed; the database-backed lease
/// (ProgrammeOps_SyncLease/SyncRunCoordinator) was already tenant-keyed.
///
/// Scope: a single process. Behind a multi-instance deployment this needs a
/// database-backed lease instead — that's exactly what SyncRunCoordinator
/// layers on top of this in-process guard.
/// </summary>
public sealed class SyncRunGuard
{
    private readonly ConcurrentDictionary<LatchKey, SemaphoreSlim> _latches = new();

    /// <summary>Returns a lease to dispose when the run ends, or null if a run for this tenant's source is already in progress.</summary>
    public IDisposable? TryEnter(Guid tenantId, string source)
    {
        var key = new LatchKey(tenantId, source);
        var latch = _latches.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return latch.Wait(0) ? new Lease(latch) : null;
    }

    public bool IsRunning(Guid tenantId, string source) =>
        _latches.TryGetValue(new LatchKey(tenantId, source), out var latch) && latch.CurrentCount == 0;

    private readonly record struct LatchKey(Guid TenantId, string Source)
    {
        public bool Equals(LatchKey other) =>
            TenantId == other.TenantId && string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(TenantId, StringComparer.OrdinalIgnoreCase.GetHashCode(Source));
    }

    private sealed class Lease(SemaphoreSlim latch) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                latch.Release();
            }
        }
    }
}
