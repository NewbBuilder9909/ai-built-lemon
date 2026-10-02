namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// An approved mapping from one external account to one staff member,
/// scoped to the connection it was approved for.
///
/// **This is the only thing that can put evidence on a person's record.**
/// Not a matching email, not a matching display name, not a similar login.
/// The design document is explicit — "name/email matching alone cannot
/// publish person-level evidence" — and the reason is that getting it
/// wrong attributes one employee's work to another, in a system a manager
/// may read before a conversation about their career.
///
/// Scoped to (<see cref="TenantId"/>, <see cref="ConnectionKey"/>,
/// <see cref="ExternalActorId"/>) rather than to the tenant alone: the same
/// GitHub login can appear in two organisations a tenant has connected, and
/// approving them separately is the honest granularity — an admin who
/// recognises someone in one org has not thereby vouched for an identically
/// named account in another.
/// </summary>
public sealed record EvidenceActorLink
{
    public required Guid LinkKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string Provider { get; init; }

    /// <summary>The provider's stable account id — not the login, which can be renamed.</summary>
    public required string ExternalActorId { get; init; }

    /// <summary>The login at approval time, kept for display and for spotting a rename.</summary>
    public string? ExternalLogin { get; init; }

    public required Guid StaffKey { get; init; }

    /// <summary>Who approved it. An identity link is an act with a name attached.</summary>
    public Guid? ApprovedByStaffKey { get; init; }

    public required DateTime ApprovedAtUtc { get; init; }
}

/// <summary>Why an actor is still unmapped — shown on the queue so the admin knows what to do about it.</summary>
public enum UnmappedActorReason
{
    /// <summary>No candidate at all. Someone has to say who this is.</summary>
    NoCandidate = 0,

    /// <summary>
    /// A staff member's email matches. This is a **suggestion only** and is
    /// displayed as one — approving it is still a human act.
    /// </summary>
    EmailSuggestion = 1,

    /// <summary>More than one staff member matched. Never resolved automatically; the admin picks.</summary>
    Ambiguous = 2,

    /// <summary>A bot or service account. Excluded from person attribution; kept so the exclusion is visible.</summary>
    Bot = 3
}

/// <summary>
/// An external account seen in evidence that has no approved link yet.
///
/// The queue exists so that unattributed work is *visible* rather than
/// silently missing. A coverage figure computed over mapped evidence alone,
/// with no indication that forty per cent of commits belong to accounts
/// nobody has identified, would be worse than no figure.
///
/// Upserted per sighting: <see cref="OccurrenceCount"/> and
/// <see cref="LastSeenUtc"/> move, so the busiest unknown account sorts to
/// the top of the admin's list.
/// </summary>
public sealed record UnmappedEvidenceActor
{
    public required Guid UnmappedActorKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string Provider { get; init; }

    public required string ExternalActorId { get; init; }

    public string? ExternalLogin { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>From a commit trailer or the provider's profile. Feeds the suggestion; never the decision.</summary>
    public string? Email { get; init; }

    public required bool IsBot { get; init; }

    public required UnmappedActorReason Reason { get; init; }

    /// <summary>
    /// The staff member an email match points at, if exactly one did.
    /// Presented to the admin as "is this them?" and nothing more — the row
    /// stays unmapped until somebody approves it.
    /// </summary>
    public Guid? SuggestedStaffKey { get; init; }

    public required int OccurrenceCount { get; init; }

    public required DateTime FirstSeenUtc { get; init; }

    public required DateTime LastSeenUtc { get; init; }

    public DateTime? ResolvedAtUtc { get; init; }

    public bool IsOpen => ResolvedAtUtc is null;
}
