namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>Which stream of artefacts a cursor belongs to. One cursor per (repository, stream).</summary>
public enum EvidenceStream
{
    Commits = 0,
    PullRequests = 1,
    Reviews = 2
}

/// <summary>
/// How much we can actually vouch for. Ordered by how bad it is, so a
/// connection's overall state is the worst of its repositories.
/// </summary>
public enum EvidenceCoverageStatus
{
    /// <summary>A run completed and stored every page it fetched.</summary>
    Complete = 0,

    /// <summary>Never synced. Not a failure — but not evidence of absence either.</summary>
    NeverRun = 1,

    /// <summary>
    /// A run stopped part way — a failed page, a rate limit, a cancellation.
    /// Previously known facts are kept; the window is not advanced. What is
    /// absent may simply not have been fetched.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// The connection can no longer read this repository. Existing evidence
    /// stays and is marked stale; it is not deleted, because it was true
    /// when it was collected.
    /// </summary>
    PermissionLost = 3,

    /// <summary>The repository left the admin's selection, or the connection was disconnected.</summary>
    OutOfScope = 4
}

/// <summary>
/// The incremental sync position and honesty record for one
/// (tenant, connection, repository, stream).
///
/// Two separate ideas live here on purpose. <see cref="Cursor"/> is where
/// to resume from. <see cref="CompleteThroughUtc"/> is how far we are
/// prepared to *claim* coverage — and it only advances when a run finishes
/// a stream cleanly. A partial run therefore leaves the claim where it was
/// while still keeping whatever it managed to store, which is the
/// difference between "we have no evidence of X" and "we have not looked".
///
/// <see cref="Cursor"/> is deliberately persisted only after the page it
/// covers has been stored, so a crash re-fetches a page rather than
/// skipping one. Re-fetching is free — the evidence upsert is idempotent on
/// the stable key — whereas skipping loses data silently.
/// </summary>
public sealed record EvidenceCoverage
{
    public required Guid CoverageKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string RepositoryKey { get; init; }

    public required EvidenceStream Stream { get; init; }

    /// <summary>Provider-specific resume token — an ISO "since" timestamp or an opaque page cursor.</summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// The start of the observation window — the earliest point this
    /// connection has ever fetched from. Shown on the portfolio so a reader
    /// knows evidence before this date is missing by construction, not
    /// because the person did nothing.
    /// </summary>
    public DateTime? ObservedFromUtc { get; init; }

    /// <summary>Only advanced by a run that completed this stream cleanly.</summary>
    public DateTime? CompleteThroughUtc { get; init; }

    public required EvidenceCoverageStatus Status { get; init; }

    /// <summary>Reader-facing, e.g. "rate limited after 3 pages". Never a raw provider body.</summary>
    public string? StatusDetail { get; init; }

    public Guid? LastRunKey { get; init; }

    public DateTime? LastAttemptedAtUtc { get; init; }

    public DateTime? LastSucceededAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public bool IsTrustworthy => Status == EvidenceCoverageStatus.Complete;
}

/// <summary>
/// What a reader needs in order to interpret an evidence portfolio: the
/// window it covers, how much of it is trustworthy, and how much work
/// belongs to accounts nobody has identified.
///
/// The design document asks for uncertainty to be as prominent as results.
/// This is that, as a type — a view cannot render the evidence without
/// also having these to hand.
/// </summary>
public sealed record EvidenceCoverageSummary
{
    public required IReadOnlyList<EvidenceCoverage> Repositories { get; init; }

    /// <summary>Open rows on the mapping queue. Work that belongs to nobody, yet.</summary>
    public required int UnmappedActors { get; init; }

    /// <summary>Evidence rows attached to no staff member because of the above.</summary>
    public required int UnattributedEvidence { get; init; }

    /// <summary>
    /// True when this connection has never completed a real run against a
    /// live installation — so the UI can label what it shows as a fixture
    /// or sandbox replay rather than letting it pass as production data.
    /// </summary>
    public required bool IsUnverifiedReplay { get; init; }

    public DateTime? ObservedFromUtc => Repositories.Where(r => r.ObservedFromUtc is not null).Min(r => r.ObservedFromUtc);

    public DateTime? CompleteThroughUtc =>
        Repositories.Count == 0 || Repositories.Any(r => r.CompleteThroughUtc is null)
            ? null
            // The weakest link: a portfolio is only complete through the
            // earliest point that *every* repository is complete through.
            : Repositories.Min(r => r.CompleteThroughUtc);

    public EvidenceCoverageStatus WorstStatus =>
        Repositories.Count == 0 ? EvidenceCoverageStatus.NeverRun : Repositories.Max(r => r.Status);

    public bool IsComplete => WorstStatus == EvidenceCoverageStatus.Complete && UnmappedActors == 0;
}
