namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// How capable someone is at a skill, on a four-point rubric.
///
/// The levels are written as *observable behaviour*, not as seniority or as
/// a score. That is deliberate and is the load-bearing decision of this
/// module: docs/staff-skills-evidence-module.md rules out deriving a level
/// from activity (commit counts, ticket counts, language bytes), because
/// none of those measure capability. A level is set by a person, reviewed
/// by a person, and carries a review date so it expires rather than rots.
///
/// Numeric values are ordered so "at or above" comparisons work, and are
/// persisted by *name* (see the DTO), so re-ordering them later would be a
/// data migration rather than a silent reinterpretation of stored rows.
/// </summary>
public enum ProficiencyLevel
{
    /// <summary>Has seen this and can follow written guidance, with someone to ask.</summary>
    Awareness = 1,

    /// <summary>Does routine work in this without supervision; escalates anything unusual.</summary>
    Working = 2,

    /// <summary>Handles unfamiliar problems in this, and reviews other people's work in it.</summary>
    Practitioner = 3,

    /// <summary>Sets the approach for this, and is who others are sent to when it is hard.</summary>
    Lead = 4
}

/// <summary>
/// The plain-language rubric shown next to every proficiency choice, and the
/// review cycle that stops a validated level standing for ever.
///
/// Pure data and pure lookups — no Umbraco types, nothing async — for the
/// same reason as <see cref="Models.Staff.RoleCapabilities"/> and
/// Services/Tenancy/PlanEntitlements: the words a manager and an employee
/// will argue over should be reviewable and unit-testable on their own.
/// </summary>
public static class ProficiencyRubric
{
    /// <summary>
    /// How long a validated assertion stands before it is due another look.
    /// Twelve months matches a normal appraisal cycle; a reviewer can set an
    /// earlier date, but never a later one than they consciously typed.
    /// </summary>
    public const int DefaultReviewIntervalMonths = 12;

    private static readonly IReadOnlyDictionary<ProficiencyLevel, string> Descriptions =
        new Dictionary<ProficiencyLevel, string>
        {
            [ProficiencyLevel.Awareness] = "Has seen this and can follow written guidance, with someone to ask.",
            [ProficiencyLevel.Working] = "Does routine work in this without supervision; escalates anything unusual.",
            [ProficiencyLevel.Practitioner] = "Handles unfamiliar problems in this, and reviews other people's work in it.",
            [ProficiencyLevel.Lead] = "Sets the approach for this, and is who others are sent to when it is hard."
        };

    /// <summary>Every level, lowest first — the order the UI offers them in.</summary>
    public static readonly IReadOnlyList<ProficiencyLevel> All =
    [
        ProficiencyLevel.Awareness,
        ProficiencyLevel.Working,
        ProficiencyLevel.Practitioner,
        ProficiencyLevel.Lead
    ];

    /// <summary>
    /// The rubric text for a level. Throws for an undefined level rather
    /// than returning an empty string: a proficiency the rubric cannot
    /// describe is a bug, and showing a blank cell would hide it.
    /// </summary>
    public static string Describe(ProficiencyLevel level) =>
        Descriptions.TryGetValue(level, out var description)
            ? description
            : throw new ArgumentOutOfRangeException(nameof(level), level, "No rubric text is defined for this proficiency level.");

    /// <summary>
    /// Counts as cover for continuity purposes. Below this, someone can do
    /// the routine work but is not a person you would leave alone with the
    /// component — see the coverage view's single-maintainer warning.
    /// </summary>
    public const ProficiencyLevel CoverThreshold = ProficiencyLevel.Practitioner;

    /// <summary>When a level validated at <paramref name="validatedAtUtc"/> falls due for review.</summary>
    public static DateOnly DefaultReviewDue(DateTime validatedAtUtc) =>
        DateOnly.FromDateTime(validatedAtUtc.AddMonths(DefaultReviewIntervalMonths));
}
