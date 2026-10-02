namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// A manager's statement that a named person owns a component, plus the
/// date they last said so.
///
/// **Ownership is declared, never inferred.** The product could guess an
/// owner from who commits most, and it deliberately does not: the person
/// who touches a component most often is frequently the person who has
/// been left holding it, which is the exposure this view exists to find,
/// not the answer to it. The design document requires a manager-owned
/// component map before key-person coverage is published at all.
///
/// <see cref="LastReviewedAtUtc"/> is load-bearing. An ownership record
/// nobody has confirmed for two years is not a plan, and the coverage
/// view reports it as stale rather than treating it as current.
///
/// <see cref="ComponentKey"/> is a plain string, aligned by the tenant
/// with whichever vocabulary they already use. It deliberately does not
/// hard-reference a <see cref="SkillDefinition"/> of kind
/// <see cref="SkillKind.Component"/>, nor a Service Ops component tag:
/// those are two different vocabularies today (a skill people declare
/// versus a product area tickets are tagged with), and forcing them into
/// one is a customer conversation, not a schema decision. Where the keys
/// happen to match, the coverage view joins them softly and says so.
/// </summary>
public sealed record ComponentOwnership
{
    public required Guid OwnershipKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>Canonical key, normalised like a skill key. Unique per tenant.</summary>
    public required string ComponentKey { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// The person accountable for the component. Null is allowed and is
    /// itself a finding — an unowned component is the most exposed kind.
    /// </summary>
    public Guid? OwnerStaffKey { get; init; }

    /// <summary>Who confirmed this record, and when. Both required to count as reviewed.</summary>
    public Guid? ReviewedByStaffKey { get; init; }

    public DateTime? LastReviewedAtUtc { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// Whether the manager's statement is still current. Twelve months
    /// matches the skills rubric's review cycle, so ownership and
    /// proficiency go stale on the same clock.
    /// </summary>
    public bool IsReviewCurrentOn(DateOnly asOf) =>
        LastReviewedAtUtc is { } reviewed
        && DateOnly.FromDateTime(reviewed).AddMonths(ProficiencyRubric.DefaultReviewIntervalMonths) >= asOf;
}

/// <summary>
/// A manager's statement that a named person is approved cover for a
/// component.
///
/// Separate from a validated skill assertion on purpose. Holding
/// Practitioner in "Billing" says somebody can do the work; being named
/// as backup says the organisation has *decided* they are the cover and
/// they know it. The design document asks for reviewed backup coverage,
/// not merely for a second person who happens to be qualified — the
/// second is a capability, the first is a plan.
/// </summary>
public sealed record ComponentBackup
{
    public required Guid BackupKey { get; init; }

    public required Guid TenantId { get; init; }

    public required string ComponentKey { get; init; }

    public required Guid StaffKey { get; init; }

    public Guid? ApprovedByStaffKey { get; init; }

    public required DateTime ApprovedAtUtc { get; init; }

    /// <summary>Why this person; what they would need in order to step in.</summary>
    public string? Note { get; init; }
}
