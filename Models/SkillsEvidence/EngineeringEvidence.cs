namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// What kind of artefact a piece of evidence is. Provider-neutral: a
/// GitHub pull request and a GitLab merge request are both
/// <see cref="PullRequest"/>, and nothing above the Bronze boundary should
/// have to know which forge produced it.
/// </summary>
public enum EvidenceSourceType
{
    Commit = 0,
    PullRequest = 1,
    Review = 2
}

/// <summary>
/// How a person was involved. These are kept **distinct**, never collapsed
/// into "contributor", because they are not the same claim about a person
/// and the design document is explicit about it: a commit committer may be
/// the person who pressed the merge button, a co-author may have been
/// pairing, and a reviewer did not write the code.
///
/// One artefact can carry several people in several roles; one person can
/// hold several roles on the same artefact. That is why role is part of
/// the evidence key rather than a column that gets overwritten.
/// </summary>
public enum EvidenceRole
{
    /// <summary>Wrote the change (git author).</summary>
    CommitAuthor = 0,

    /// <summary>Applied the change (git committer) — frequently a rebase, squash or merge, not authorship.</summary>
    CommitCommitter = 1,

    /// <summary>Named in a Co-authored-by trailer. Usually pairing; never inferred, only read from the trailer.</summary>
    CoAuthor = 2,

    /// <summary>Opened the pull request.</summary>
    PullRequestAuthor = 3,

    /// <summary>Reviewed a pull request.</summary>
    Reviewer = 4
}

/// <summary>
/// Whether this evidence has been tied to a person, and on what authority.
///
/// The load-bearing value is <see cref="Unmapped"/>. Slice 2's rule is that
/// an external account becomes a named staff member **only** through an
/// explicitly approved <see cref="EvidenceActorLink"/> — never through a
/// display name, and never through an email match alone. An email match is
/// allowed to appear as a *suggestion* on the review queue; it is not
/// allowed to publish anything to a person's profile.
/// </summary>
public enum EvidenceAttributionStatus
{
    /// <summary>No approved link. Counts towards the visible unmapped queue and reaches nobody's portfolio.</summary>
    Unmapped = 0,

    /// <summary>An approved link exists. The only status that puts evidence on a person's record.</summary>
    Mapped = 1,

    /// <summary>A bot or service account, excluded from person attribution entirely.</summary>
    Bot = 2,

    /// <summary>More than one candidate. Deliberately not resolved — queued with the reason.</summary>
    Ambiguous = 3
}

/// <summary>
/// One person's involvement in one source artefact — the Silver contract
/// for engineering evidence, and deliberately provider-neutral. A GitHub
/// adapter writes it; a future GitLab or plain-git adapter writes the same
/// shape; the portfolio and coverage views read only this.
///
/// **This is evidence of participation, not of proficiency.** Nothing here
/// can raise a <see cref="StaffSkillAssertion"/>. A reviewer reads it
/// alongside a person's declared skills and decides for themselves; the
/// two are joined in the view, never in the data.
///
/// The stable key is (<see cref="TenantId"/>, <see cref="ConnectionKey"/>,
/// <see cref="SourceType"/>, <see cref="ExternalId"/>, <see cref="Role"/>).
/// <see cref="ConnectionKey"/> is in the key — not just the tenant — because
/// one tenant may connect two GitHub organisations, and the same numeric id
/// can mean different artefacts in each. <see cref="SourceAccountId"/> is
/// carried alongside so a row remains self-describing if a connection is
/// removed and recreated.
///
/// No code body, no diff, no patch text: only metadata and a link back to
/// the source, per the design document's data-minimisation rule.
/// </summary>
public sealed record EngineeringEvidence
{
    public required Guid EvidenceKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>The <see cref="EvidenceConnection"/> this came through.</summary>
    public required Guid ConnectionKey { get; init; }

    /// <summary>Stable provider name, e.g. "GitHub". Never localised.</summary>
    public required string Provider { get; init; }

    /// <summary>The source's own account — a GitHub organisation login/id. Part of what makes the key stable.</summary>
    public required string SourceAccountId { get; init; }

    public required EvidenceSourceType SourceType { get; init; }

    /// <summary>The provider's own id for the artefact — a commit SHA, a PR node id, a review id.</summary>
    public required string ExternalId { get; init; }

    public required EvidenceRole Role { get; init; }

    /// <summary>The provider's stable account id for the actor. Preferred over the login, which can be renamed.</summary>
    public string? ActorExternalId { get; init; }

    /// <summary>The actor's login at the time of ingestion — for display and for the mapping queue only.</summary>
    public string? ActorLogin { get; init; }

    /// <summary>
    /// A commit trailer can name someone who has no provider account at
    /// all. Kept for the mapping queue as a *suggestion* input; on its own
    /// it never maps anyone.
    /// </summary>
    public string? ActorEmail { get; init; }

    public required bool ActorIsBot { get; init; }

    /// <summary>Set only when an approved <see cref="EvidenceActorLink"/> exists. Null otherwise, always.</summary>
    public Guid? StaffKey { get; init; }

    public required EvidenceAttributionStatus AttributionStatus { get; init; }

    /// <summary>"owner/name" — the repository the artefact belongs to.</summary>
    public required string RepositoryKey { get; init; }

    /// <summary>A PR or commit title. Metadata, not content.</summary>
    public string? Title { get; init; }

    /// <summary>Link back to the artefact on the provider, so every claim can be checked at source.</summary>
    public string? SourceUrl { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// Languages and components suggested by the changed file paths.
    /// **Unvalidated evidence.** It means "participated in changes to files
    /// classified this way during this period", never "is proficient".
    /// </summary>
    public IReadOnlyList<string> LanguageHints { get; init; } = [];

    /// <summary>
    /// For a commit row: whether the provider verified that this person made
    /// the commit. A commit's author and co-author fields are free text, so
    /// anyone can name a colleague; only a verified signature by the same
    /// person counts. True for an author who is also the verified signer, or
    /// a verified committer; false for an unsigned or unverifiable commit and
    /// for every co-author trailer; null where it was not recorded (rows
    /// ingested before schema version 2, or a provider that does not say).
    /// Pull requests and reviews are authenticated actions and leave it null.
    /// </summary>
    public bool? AuthorshipVerified { get; init; }

    /// <summary>
    /// A commit row whose authorship is not verified. Shown as "authorship not
    /// verified" wherever the row is, never hidden: it is still a record of
    /// the commit, just not proof of who wrote it.
    /// </summary>
    public bool AuthorshipUnverified => SourceType == EvidenceSourceType.Commit && AuthorshipVerified != true;

    /// <summary>The sync run that last wrote this row — provenance for a partial or replayed ingest.</summary>
    public Guid? ObservedInRunKey { get; init; }

    public required int SchemaVersion { get; init; }

    public required DateTime FirstIngestedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>Only mapped, non-bot evidence belongs on a person's portfolio.</summary>
    public bool IsAttributableToAPerson =>
        AttributionStatus == EvidenceAttributionStatus.Mapped && StaffKey is not null && !ActorIsBot;
}

/// <summary>
/// The current schema version of the evidence contract. Stored on every
/// row so a later mapper change can tell which rows were written under
/// which rules, instead of inferring it from a timestamp.
/// </summary>
public static class EngineeringEvidenceSchema
{
    /// <summary>2: commit rows record <see cref="EngineeringEvidence.AuthorshipVerified"/>.</summary>
    public const int CurrentVersion = 2;
}
