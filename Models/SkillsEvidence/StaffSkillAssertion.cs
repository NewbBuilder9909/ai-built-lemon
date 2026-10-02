namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// Who wrote an assertion down. Kept separate from
/// <see cref="AssertionStatus"/> because "the employee said so and a manager
/// agreed" and "a manager recorded it and nobody asked the employee" are
/// both <see cref="AssertionStatus.Validated"/>, and a reader is entitled to
/// know which one they are looking at.
/// </summary>
public enum AssertionOrigin
{
    /// <summary>The staff member declared it about themselves.</summary>
    SelfDeclared = 0,

    /// <summary>A reviewer recorded it on the staff member's behalf.</summary>
    ReviewerRecorded = 1
}

/// <summary>
/// Where an assertion has got to. Every transition writes a *new* row that
/// supersedes the old one, so this is the status of one point in a history,
/// never a field that gets overwritten.
/// </summary>
public enum AssertionStatus
{
    /// <summary>Declared, waiting for a reviewer. Counts as a claim, not as evidence.</summary>
    Submitted = 0,

    /// <summary>A reviewer agreed. The only status that counts towards coverage.</summary>
    Validated = 1,

    /// <summary>A reviewer disagreed, with a rationale. Stays visible to the staff member.</summary>
    Rejected = 2,

    /// <summary>
    /// The staff member disputes the current record — the correction flow.
    /// Back in the reviewer's queue, and deliberately *not* counted as
    /// coverage while it is contested.
    /// </summary>
    ChallengeRaised = 3,

    /// <summary>The staff member no longer claims this skill. Terminal until they declare it again.</summary>
    Withdrawn = 4
}

/// <summary>
/// One person's claim about one skill, at one point in time.
///
/// The table is append-only: validating, rejecting, challenging or
/// withdrawing all insert a new row and stamp
/// <see cref="SupersededAtUtc"/> on the previous one. That is what
/// "preserve assertion history rather than overwriting it" means in
/// docs/staff-skills-evidence-module.md — the row a coverage query reads is
/// simply the one whose <see cref="SupersededAtUtc"/> is null, which a
/// filtered unique index keeps to at most one per (tenant, staff, skill).
///
/// This is personal data. It is deleted outright — not pseudonymised — when
/// the subject is erased; see Services/SkillsEvidence/SkillsEvidenceDataParticipant
/// and docs/gdpr.md.
/// </summary>
public sealed record StaffSkillAssertion
{
    public required Guid AssertionKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid StaffKey { get; init; }

    /// <summary>References <see cref="SkillDefinition.SkillKey"/> within the same tenant.</summary>
    public required string SkillKey { get; init; }

    public required ProficiencyLevel Proficiency { get; init; }

    public required AssertionOrigin Origin { get; init; }

    public required AssertionStatus Status { get; init; }

    /// <summary>
    /// What the staff member says supports this, in their own words. Free
    /// text, so it is the part of the record that is genuinely revealing
    /// about a person — which is why it never reaches the audit log, and
    /// goes on erasure with the rest of the row.
    /// </summary>
    public string? EvidenceNote { get; init; }

    public Guid? ReviewerStaffKey { get; init; }

    public DateTime? ReviewedAtUtc { get; init; }

    /// <summary>The reviewer's rationale. Required to reject; see ISkillAssertionService.</summary>
    public string? ReviewNote { get; init; }

    /// <summary>
    /// When this validated level falls due for another look. Null until
    /// validated. Past this date the assertion still reads as validated but
    /// the coverage view reports it as stale — an expired review is a known
    /// unknown, not a silent downgrade.
    /// </summary>
    public DateOnly? ReviewDueOn { get; init; }

    /// <summary>Who wrote this particular row — the staff member, or the reviewer.</summary>
    public required Guid RecordedByStaffKey { get; init; }

    public required DateTime RecordedAtUtc { get; init; }

    /// <summary>The row this one replaced, so the chain can be walked backwards.</summary>
    public Guid? SupersedesAssertionKey { get; init; }

    /// <summary>Null on exactly one row per (tenant, staff, skill) — the current one.</summary>
    public DateTime? SupersededAtUtc { get; init; }

    public bool IsCurrent => SupersededAtUtc is null;

    /// <summary>
    /// Whether this counts as reviewed cover for a skill. Deliberately
    /// stricter than "status is Validated": a validated row whose review
    /// date has passed is no longer evidence of anything current.
    /// </summary>
    public bool CountsAsValidatedOn(DateOnly asOf) =>
        IsCurrent
        && Status == AssertionStatus.Validated
        && (ReviewDueOn is null || ReviewDueOn.Value >= asOf);
}
