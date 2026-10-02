using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// In-memory ISyncRunRepository reproducing the lease semantics of the
/// NPoco one: a (tenant, source) lease is taken only when unowned or
/// expired, a stale Running run for that (tenant, source) is marked Failed
/// (abandoned) by the next acquirer, heartbeats and completions only count
/// for the current (owner, runKey) and report whether they did, and
/// Fail/Complete only transition a still-Running row. The SQL implementation
/// judges expiry on the database clock; here the caller's nowUtc stands in
/// for it. GetLatestAcrossTenantsAsync/GetLatestSuccessfulAcrossTenantsAsync
/// mirror the real repository's deliberate cross-tenant reads for the
/// platform console.
/// </summary>
public sealed class FakeSyncRunRepository : ISyncRunRepository
{
    public readonly List<SyncRun> Runs = [];
    public readonly Dictionary<(Guid TenantId, string Source), (string? Owner, DateTime? ExpiresAtUtc, Guid? RunKey)> Leases = new(new LeaseKeyComparer());
    public int HeartbeatCount { get; private set; }
    public int RefusedHeartbeats { get; private set; }
    public int RefusedCompletions { get; private set; }
    public DateTime? LastPurgeCutoff { get; private set; }

    public Task<SyncRun?> TryAcquireAsync(Guid tenantId, string source, string instanceId, int? triggeredByMemberId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        var key = (tenantId, source);
        var lease = Leases.GetValueOrDefault(key);
        if (lease.Owner is not null && lease.ExpiresAtUtc is not null && lease.ExpiresAtUtc >= nowUtc)
        {
            return Task.FromResult<SyncRun?>(null);
        }

        foreach (var stale in Runs.Where(r => r.TenantId == tenantId && r.Source == source && r.Status == SyncRunStatus.Running).ToList())
        {
            Runs.Remove(stale);
            Runs.Add(stale with { Status = SyncRunStatus.Failed, Error = SyncRunRepository.AbandonedError, FinishedAtUtc = nowUtc });
        }

        var run = new SyncRun
        {
            RunKey = Guid.NewGuid(),
            TenantId = tenantId,
            Source = source,
            Status = SyncRunStatus.Running,
            StartedAtUtc = nowUtc,
            HeartbeatAtUtc = nowUtc,
            TriggeredByMemberId = triggeredByMemberId,
            InstanceId = instanceId,
            Stage = "starting"
        };
        Runs.Add(run);
        Leases[key] = (instanceId, nowUtc.Add(leaseDuration), run.RunKey);
        return Task.FromResult<SyncRun?>(run);
    }

    public Task<bool> HeartbeatAsync(Guid runKey, Guid tenantId, string source, string instanceId, string? stage, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (!Owns(tenantId, source, instanceId, runKey))
        {
            RefusedHeartbeats++;
            return Task.FromResult(false);
        }

        Leases[(tenantId, source)] = (instanceId, nowUtc.Add(leaseDuration), runKey);
        Replace(runKey, r => r.Status == SyncRunStatus.Running ? r with { HeartbeatAtUtc = nowUtc, Stage = stage } : r);
        HeartbeatCount++;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteAsync(Guid runKey, Guid tenantId, string source, string instanceId, string summary, string summaryJson, DateTime nowUtc)
    {
        if (!Owns(tenantId, source, instanceId, runKey) || Runs.First(r => r.RunKey == runKey).Status != SyncRunStatus.Running)
        {
            RefusedCompletions++;
            return Task.FromResult(false);
        }

        Leases[(tenantId, source)] = (instanceId, nowUtc.AddSeconds(60), runKey);
        Replace(runKey, r => r with { Status = SyncRunStatus.Succeeded, FinishedAtUtc = nowUtc, HeartbeatAtUtc = nowUtc, Summary = summary, SummaryJson = summaryJson, Stage = null });
        return Task.FromResult(true);
    }

    public Task FailAsync(Guid runKey, string? stage, string error, DateTime nowUtc)
    {
        Replace(runKey, r => r.Status == SyncRunStatus.Running
            ? r with { Status = SyncRunStatus.Failed, FinishedAtUtc = nowUtc, HeartbeatAtUtc = nowUtc, Stage = stage, Error = error }
            : r);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid tenantId, string source, string instanceId, Guid runKey)
    {
        if (Owns(tenantId, source, instanceId, runKey))
        {
            Leases[(tenantId, source)] = (null, null, null);
        }

        return Task.CompletedTask;
    }

    public Task<SyncRun?> GetLatestAsync(Guid tenantId, string source) =>
        Task.FromResult(Runs.Where(r => r.TenantId == tenantId && r.Source == source).OrderByDescending(r => r.StartedAtUtc).FirstOrDefault());

    public Task<SyncRun?> GetLatestSuccessfulAsync(Guid tenantId, string source) =>
        Task.FromResult(Runs.Where(r => r.TenantId == tenantId && r.Source == source && r.Status == SyncRunStatus.Succeeded).OrderByDescending(r => r.FinishedAtUtc).FirstOrDefault());

    public Task<SyncRun?> GetRunningAsync(Guid tenantId, string source) =>
        Task.FromResult(Runs.FirstOrDefault(r => r.TenantId == tenantId && r.Source == source && r.Status == SyncRunStatus.Running));

    public Task<SyncRun?> GetLatestAcrossTenantsAsync(string source) =>
        Task.FromResult(Runs.Where(r => r.Source == source).OrderByDescending(r => r.StartedAtUtc).FirstOrDefault());

    public Task<SyncRun?> GetLatestSuccessfulAcrossTenantsAsync(string source) =>
        Task.FromResult(Runs.Where(r => r.Source == source && r.Status == SyncRunStatus.Succeeded).OrderByDescending(r => r.FinishedAtUtc).FirstOrDefault());

    public Task<int> DeleteFinishedOlderThanAsync(DateTime cutoffUtc)
    {
        LastPurgeCutoff = cutoffUtc;
        return Task.FromResult(Runs.RemoveAll(r => r.Status != SyncRunStatus.Running && r.FinishedAtUtc < cutoffUtc));
    }

    public SyncRun Single(string source) => Runs.Single(r => r.Source == source);

    private bool Owns(Guid tenantId, string source, string instanceId, Guid runKey)
    {
        var lease = Leases.GetValueOrDefault((tenantId, source));
        return lease.Owner == instanceId && lease.RunKey == runKey;
    }

    private void Replace(Guid runKey, Func<SyncRun, SyncRun> update)
    {
        var existing = Runs.First(r => r.RunKey == runKey);
        Runs.Remove(existing);
        Runs.Add(update(existing));
    }

    private sealed class LeaseKeyComparer : IEqualityComparer<(Guid TenantId, string Source)>
    {
        public bool Equals((Guid TenantId, string Source) x, (Guid TenantId, string Source) y) =>
            x.TenantId == y.TenantId && string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid TenantId, string Source) obj) =>
            HashCode.Combine(obj.TenantId, obj.Source.ToUpperInvariant());
    }
}
