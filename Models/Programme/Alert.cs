namespace ProgrammePulse.Models.Programme;

/// <summary>
/// The minimal in-app notification channel this build plan called for
/// before threshold alerts could exist — see IAlertDetectionService's doc
/// comment for why in-app rather than email/Slack. EntityKey is a
/// WorkstreamKey for WorkstreamBlocked, a StaffKey for
/// NegativeResidualCapacity — the (Type, EntityKey) pair is how detection
/// avoids raising the same alert again on every Reporting Hub page load
/// while the underlying condition is still true.
/// </summary>
public sealed record Alert
{
    public required Guid AlertKey { get; init; }

    public Guid? TenantId { get; init; }

    public required AlertType Type { get; init; }

    public required Guid EntityKey { get; init; }

    public required string Message { get; init; }

    public required DateTime RaisedAtUtc { get; init; }

    public DateTime? AcknowledgedAtUtc { get; init; }

    public Guid? AcknowledgedByStaffKey { get; init; }
}
