namespace ProgrammePulse.Services.Integrations.Resilience;

/// <summary>
/// Thrown by <see cref="SyncRunHandle"/> when a heartbeat or completion
/// finds that this instance no longer owns the source's lease: this worker
/// stalled for longer than ProgrammeOps:SyncLeaseSeconds and another
/// instance took the lease over and marked this run abandoned. The worker
/// must stop — anything it wrote after the takeover is stale, and the run
/// it would "complete" is no longer its own. The other instance's run is
/// the live one, so the caller should not simply re-run.
/// </summary>
public sealed class SyncLeaseLostException(string source, Guid runKey)
    : InvalidOperationException(
        $"This instance lost the {source} sync lease (run {runKey:N}): it stopped heartbeating for longer than the lease and another instance took the source over. " +
        "Processing stopped here; the other instance's run is the live one. Check the Data sources panel for its outcome.")
{
    public string SyncSource { get; } = source;

    public Guid RunKey { get; } = runKey;
}
