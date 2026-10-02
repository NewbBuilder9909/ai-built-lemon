namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A person a sync saw in an external source but could not match to a
/// StaffProfile. Recorded (never silently dropped) so an Admin can see
/// exactly whose work and bookings are missing from capacity and workload
/// figures, and resolve it by creating an <see cref="ExternalIdentityLink"/>,
/// or by approving the <see cref="SuggestedStaffKey"/> an email match offers.
/// One row per (source, external user id, email); OccurrenceCount and
/// LastSeenUtc grow on every sync that still can't resolve it. A resolved
/// row keeps its ResolvedStaffKey/ResolvedAtUtc as history and leaves the
/// queue.
/// </summary>
public sealed record UnresolvedIdentity
{
    public required Guid UnresolvedIdentityKey { get; init; }

    public Guid? TenantId { get; init; }

    public required string ExternalSource { get; init; }

    public string? ExternalUserId { get; init; }

    public string? Email { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Where it was seen, e.g. "task assignee", "time entry user", "booking resource" — the latest context wins.</summary>
    public required string Context { get; init; }

    public required DateTime FirstSeenUtc { get; init; }

    public required DateTime LastSeenUtc { get; init; }

    public required int OccurrenceCount { get; init; }

    /// <summary>
    /// The one active staff profile whose email matches this person, offered
    /// for an Admin to approve. A suggestion attributes nothing: until an
    /// Admin approves it the person stays unresolved, because anyone who can
    /// set an email in the source tool could otherwise take a colleague's name
    /// (Aikido: business logic bypass). Null when nothing matched, or when more
    /// than one profile did (the row is then ambiguous).
    /// </summary>
    public Guid? SuggestedStaffKey { get; init; }

    public Guid? ResolvedStaffKey { get; init; }

    public DateTime? ResolvedAtUtc { get; init; }

    public bool IsResolved => ResolvedAtUtc is not null;
}
