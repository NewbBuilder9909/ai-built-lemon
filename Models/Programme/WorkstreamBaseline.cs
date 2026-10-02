namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A one-time snapshot of a workstream's estimated hours, locked explicitly
/// via StaffReportingController.LockBaseline rather than captured
/// automatically on sync — a baseline that silently re-locks on every
/// ClickUp re-sync would defeat the point (a slipping estimate would keep
/// erasing its own variance, exactly the gap this closes). Once a row
/// exists for a WorkstreamKey it is not overwritten.
/// </summary>
public sealed record WorkstreamBaseline
{
    public required Guid WorkstreamKey { get; init; }

    public Guid? TenantId { get; init; }

    public required decimal BaselineHours { get; init; }

    public required DateTime LockedAtUtc { get; init; }

    public Guid? LockedByStaffKey { get; init; }
}
