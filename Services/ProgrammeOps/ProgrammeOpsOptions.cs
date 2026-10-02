namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Bound from configuration section "ProgrammeOps".
/// </summary>
public sealed class ProgrammeOpsOptions
{
    public const string SectionName = "ProgrammeOps";

    /// <summary>Legacy single-account development convenience. Production startup rejects true.</summary>
    public bool AllowSharedSourceCredentials { get; set; }

    /// <summary>
    /// How long verbatim Bronze captures (ProgrammeOps_RawClickUpPayload,
    /// ProgrammeOps_RawHubPlannerPayload) are kept. Every successful sync
    /// deletes rows fetched before now − this many days. 0 disables the purge
    /// (keep forever). Bronze rows are diagnostics/replay material, not the
    /// system of record — Silver keeps the mapped data regardless. See
    /// docs/data-governance.md.
    /// </summary>
    public int RawPayloadRetentionDays { get; set; } = 90;

    /// <summary>
    /// How long a sync run may go without a heartbeat before another
    /// instance may treat it as abandoned and take the lease
    /// (ProgrammeOps_SyncLease). Heartbeats happen on every stage change
    /// and at least every third of this window, so the value only needs
    /// to exceed the longest single upstream call (30s client timeout ×
    /// up to 3 retries). Floor of 30s is enforced.
    /// </summary>
    public int SyncLeaseSeconds { get; set; } = 300;

    /// <summary>
    /// How long finished ProgrammeOps_SyncRun rows are kept for the
    /// freshness/history views. 0 disables the purge. Purged after every
    /// successful sync, alongside the Bronze purge.
    /// </summary>
    public int SyncRunHistoryRetentionDays { get; set; } = 180;
}
