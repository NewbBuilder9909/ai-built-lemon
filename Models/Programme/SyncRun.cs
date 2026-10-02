namespace ProgrammePulse.Models.Programme;

public enum SyncRunStatus
{
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// Durable record of one sync run per source — the operational state
/// behind "when was this data last published, is a run in flight, what
/// went wrong". Distinct from ProgrammeOps_AuditLog (which stays the
/// human audit trail of who triggered what): this table is what Gold reads
/// to show freshness, and what the database lease (ProgrammeOps_SyncLease)
/// points at, so a second application instance sees a run started by the
/// first. A Running row whose lease has expired is marked Failed
/// ("abandoned") by the next run that acquires the lease, so a crashed
/// process never leaves a permanently "running" source.
/// </summary>
public sealed record SyncRun
{
    public required Guid RunKey { get; init; }

    public Guid? TenantId { get; init; }

    public required string Source { get; init; }

    public required SyncRunStatus Status { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public required DateTime HeartbeatAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; init; }

    public int? TriggeredByMemberId { get; init; }

    public required string InstanceId { get; init; }

    public string? Stage { get; init; }

    /// <summary>Human-readable outcome, e.g. "3 projects, 41 bookings, 2 unmatched people".</summary>
    public string? Summary { get; init; }

    public string? SummaryJson { get; init; }

    public string? Error { get; init; }
}
