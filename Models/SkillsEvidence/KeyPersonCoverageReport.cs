namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// How exposed the organisation is on one component, combining the
/// manager's map with the reviewed skills underneath it.
///
/// This row *does* name the owner and backups — unlike
/// <see cref="SkillCoverageRow"/>, which structurally cannot. That is a
/// deliberate difference, not an inconsistency: a continuity plan whose
/// owner is anonymous is not a plan. It is why the continuity view sits
/// behind <c>ViewStaffSkillEvidence</c>, the narrower person-level
/// grant, rather than the wider <c>ViewTeamSkillCoverage</c>.
///
/// Everything here is a statement about the **organisation's exposure**.
/// One reviewed maintainer and no backup is a risk to the business
/// whoever that person is, and the wording throughout says so.
/// </summary>
public sealed record ComponentCoverageRow
{
    public required string ComponentKey { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Null when nobody has been made accountable — the most exposed state there is.</summary>
    public required Guid? OwnerStaffKey { get; init; }

    public required string? OwnerName { get; init; }

    /// <summary>Manager-approved cover. Distinct from "people who happen to be qualified".</summary>
    public required IReadOnlyList<(Guid StaffKey, string Name)> ApprovedBackups { get; init; }

    public required DateTime? LastReviewedAtUtc { get; init; }

    public required bool IsReviewCurrent { get; init; }

    /// <summary>
    /// People with a current, validated, in-date skill assertion at or
    /// above the cover threshold for the matching skill key — when the
    /// tenant's skill and component vocabularies align. Null when no
    /// skill of that key exists, which is different from zero.
    /// </summary>
    public required int? ValidatedSkillCover { get; init; }

    public required int OpenActions { get; init; }

    public required int OverdueActions { get; init; }

    /// <summary>
    /// The business-continuity finding. Nobody approved as cover, whether
    /// or not somebody is qualified: the design document asks for
    /// *reviewed* backup coverage, and a qualified colleague nobody has
    /// nominated is a capability, not a plan.
    /// </summary>
    public bool HasNoApprovedBackup => ApprovedBackups.Count == 0;

    public bool HasNoOwner => OwnerStaffKey is null;

    /// <summary>
    /// One accountable person, nobody approved to cover them, and the
    /// map is current enough to believe. The headline exposure.
    /// </summary>
    public bool IsSinglePersonExposure => !HasNoOwner && HasNoApprovedBackup;

    /// <summary>
    /// The map says one thing and the reviewed skills say another —
    /// approved backups exist but none of them holds a validated skill
    /// at cover level. Worth surfacing rather than resolving: the plan
    /// may be fine and the skills record stale, or the reverse.
    /// </summary>
    public required bool BackupsLackValidatedSkill { get; init; }
}

/// <summary>
/// Key-person coverage across a tenant, with the caveats that decide how
/// much weight it can carry.
///
/// The design document is explicit that coverage is published only after
/// a manager has validated component ownership. <see cref="IsPublishable"/>
/// is that rule as a property: until there is a reviewed map, the view
/// shows what is missing rather than a confident-looking risk register
/// built on nothing.
/// </summary>
public sealed record KeyPersonCoverageReport
{
    public required IReadOnlyList<ComponentCoverageRow> Components { get; init; }

    public required DateOnly AsOf { get; init; }

    /// <summary>Components whose ownership record has never been reviewed, or has gone stale.</summary>
    public required int ComponentsWithStaleReview { get; init; }

    /// <summary>
    /// True when the tenant's skill taxonomy contains no component-kind
    /// skills, so the reviewed-skill column is empty everywhere. Said out
    /// loud rather than shown as a column of dashes.
    /// </summary>
    public required bool SkillVocabularyUnaligned { get; init; }

    public int SinglePersonExposures => Components.Count(c => c.IsSinglePersonExposure);

    public int UnownedComponents => Components.Count(c => c.HasNoOwner);

    public int OpenActions => Components.Sum(c => c.OpenActions);

    public int OverdueActions => Components.Sum(c => c.OverdueActions);

    /// <summary>
    /// Whether this is worth publishing as a risk view. A map nobody has
    /// reviewed produces findings nobody should act on.
    /// </summary>
    public bool IsPublishable =>
        Components.Count > 0 && ComponentsWithStaleReview < Components.Count;
}
