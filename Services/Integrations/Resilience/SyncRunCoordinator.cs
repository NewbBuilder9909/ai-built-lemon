using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.Resilience;

/// <summary>
/// Starts, heartbeats and finishes a durable sync run (ProgrammeOps_SyncRun)
/// under a per-source lease (ProgrammeOps_SyncLease). Layers two guards:
/// the in-process SyncRunGuard fails fast without a database round-trip
/// when this instance already has the source in flight, and the database
/// lease refuses a run when any other instance does — which is what a
/// multi-node deployment needs (docs/release-readiness.md R17).
/// Both sync services go through <see cref="TryBeginAsync"/> and drive the
/// returned <see cref="SyncRunHandle"/>; nothing else touches the lease.
/// </summary>
public sealed class SyncRunCoordinator(
    SyncRunGuard guard,
    ISyncRunRepository runs,
    IOptions<ProgrammeOpsOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Stable for the life of this process; distinguishes lease owners across instances and restarts.</summary>
    public static readonly string InstanceId = BuildInstanceId();

    private static string BuildInstanceId()
    {
        var id = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";
        return id.Length <= 128 ? id : id[..128];
    }

    public async Task<SyncRunHandle?> TryBeginAsync(Guid tenantId, string source, int? triggeredByMemberId)
    {
        // Keyed by (tenantId, source): two different tenants triggering the
        // same source at the same moment no longer serialize against each
        // other in-process, matching the database lease below.
        var localLease = guard.TryEnter(tenantId, source);
        if (localLease is null)
        {
            return null;
        }

        var leaseDuration = TimeSpan.FromSeconds(Math.Max(30, options.Value.SyncLeaseSeconds));
        SyncRun? run;
        try
        {
            run = await runs.TryAcquireAsync(tenantId, source, InstanceId, triggeredByMemberId, timeProvider.GetUtcNow().UtcDateTime, leaseDuration);
        }
        catch
        {
            localLease.Dispose();
            throw;
        }

        if (run is null)
        {
            localLease.Dispose();
            return null;
        }

        return new SyncRunHandle(run, tenantId, localLease, runs, timeProvider, leaseDuration);
    }
}

/// <summary>
/// The in-flight run. <see cref="ReportStageAsync"/> records progress and
/// extends the lease; <see cref="TouchAsync"/> is the cheap inner-loop
/// heartbeat (a database write only when the last one is older than a
/// third of the lease). Disposing without Complete/Fail marks the run
/// Failed rather than leaving it Running forever.
///
/// Lease loss is propagated, not swallowed: any heartbeat that finds this
/// instance is no longer the owner throws <see cref="SyncLeaseLostException"/>
/// and every later call throws it too, so a worker that stalled past its
/// lease and was taken over stops at its next touch instead of carrying
/// on. Sync services therefore call <see cref="TouchAsync"/> <b>before</b>
/// each Silver write, which bounds the stale-write window to a single
/// pause that begins after a successful touch and outlasts the whole
/// lease. <see cref="CompleteAsync"/> is fenced the same way: a completion
/// the database refuses (lease gone, or the run already marked abandoned
/// by the new owner) throws rather than reporting success.
/// </summary>
public sealed class SyncRunHandle : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly IDisposable _localLease;
    private readonly ISyncRunRepository _runs;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;
    private DateTime _lastHeartbeatUtc;
    private string? _stage;
    private bool _finished;
    private bool _leaseLost;

    internal SyncRunHandle(SyncRun run, Guid tenantId, IDisposable localLease, ISyncRunRepository runs, TimeProvider timeProvider, TimeSpan leaseDuration)
    {
        Run = run;
        _tenantId = tenantId;
        _localLease = localLease;
        _runs = runs;
        _timeProvider = timeProvider;
        _leaseDuration = leaseDuration;
        _lastHeartbeatUtc = run.HeartbeatAtUtc;
        _stage = run.Stage;
    }

    public SyncRun Run { get; }

    public string? Stage => _stage;

    /// <summary>True once a heartbeat or completion found another instance owns the lease.</summary>
    public bool LeaseLost => _leaseLost;

    public async Task ReportStageAsync(string stage)
    {
        _stage = stage;
        await HeartbeatAsync();
    }

    /// <summary>
    /// Call before each write. Cheap while the lease is comfortably live;
    /// renews it (and so re-checks ownership) once a third of the lease
    /// has elapsed since the last renewal, which also covers the case
    /// where this worker was paused for longer than the whole lease.
    /// </summary>
    public async Task TouchAsync()
    {
        ThrowIfLeaseLost();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastHeartbeatUtc >= _leaseDuration / 3)
        {
            await HeartbeatAsync();
        }
    }

    public async Task CompleteAsync(string summary, object summaryPayload)
    {
        ThrowIfLeaseLost();
        var completed = await _runs.CompleteAsync(Run.RunKey, _tenantId, Run.Source, SyncRunCoordinator.InstanceId, summary, JsonSerializer.Serialize(summaryPayload), _timeProvider.GetUtcNow().UtcDateTime);
        if (!completed)
        {
            _leaseLost = true;
            _finished = true;
            throw new SyncLeaseLostException(Run.Source, Run.RunKey);
        }

        _finished = true;
        await _runs.ReleaseAsync(_tenantId, Run.Source, SyncRunCoordinator.InstanceId, Run.RunKey);
    }

    public async Task FailAsync(Exception exception)
    {
        _finished = true;
        // Status-guarded in the repository: if the new owner already marked
        // this run abandoned, this is a no-op rather than an overwrite.
        await _runs.FailAsync(Run.RunKey, _stage, $"{exception.GetType().Name}: {exception.Message}", _timeProvider.GetUtcNow().UtcDateTime);
        await _runs.ReleaseAsync(_tenantId, Run.Source, SyncRunCoordinator.InstanceId, Run.RunKey);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_finished)
            {
                _finished = true;
                await _runs.FailAsync(Run.RunKey, _stage, "Run ended without completing (cancelled or the process shut down).", _timeProvider.GetUtcNow().UtcDateTime);
                await _runs.ReleaseAsync(_tenantId, Run.Source, SyncRunCoordinator.InstanceId, Run.RunKey);
            }
        }
        finally
        {
            _localLease.Dispose();
        }
    }

    private async Task HeartbeatAsync()
    {
        ThrowIfLeaseLost();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var stillOwner = await _runs.HeartbeatAsync(Run.RunKey, _tenantId, Run.Source, SyncRunCoordinator.InstanceId, _stage, now, _leaseDuration);
        if (!stillOwner)
        {
            _leaseLost = true;
            throw new SyncLeaseLostException(Run.Source, Run.RunKey);
        }

        _lastHeartbeatUtc = now;
    }

    private void ThrowIfLeaseLost()
    {
        if (_leaseLost)
        {
            throw new SyncLeaseLostException(Run.Source, Run.RunKey);
        }
    }
}
