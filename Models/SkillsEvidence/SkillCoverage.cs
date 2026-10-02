namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// Aggregate cover for one skill across a tenant. Deliberately carries **no
/// person-level field at all** — no names, no staff keys, no per-person
/// levels — because it is readable with <c>ViewTeamSkillCoverage</c>, which
/// is a wider grant than the <c>ViewStaffSkillEvidence</c> needed to see who
/// is behind the numbers.
///
/// Same structural instinct as ProgrammeOverviewViewModel and cost data
/// (CLAUDE.md, "Cost/rate data isolation is structural, not a hidden UI
/// column"): the shape of the record makes the leak impossible, rather than
/// a view remembering not to print a column.
/// SkillCoverageShapeTests fails the build if a name or key is added here.
/// </summary>
public sealed record SkillCoverageRow
{
    public required string SkillKey { get; init; }

    public required string Name { get; init; }

    public required SkillKind Kind { get; init; }

    /// <summary>People with a current, validated, in-date assertion at or above <see cref="ProficiencyRubric.CoverThreshold"/>.</summary>
    public required int ValidatedCover { get; init; }

    /// <summary>People with a current, validated, in-date assertion at any level.</summary>
    public required int ValidatedAtAnyLevel { get; init; }

    /// <summary>Claims nobody has reviewed yet. Shown as a gap in the evidence, not as cover.</summary>
    public required int AwaitingReview { get; init; }

    /// <summary>Validated assertions whose review date has passed — cover we can no longer vouch for.</summary>
    public required int ReviewOverdue { get; init; }

    /// <summary>Assertions the staff member is currently disputing.</summary>
    public required int Challenged { get; init; }

    /// <summary>
    /// The business-continuity flag. Says something about the *organisation's
    /// exposure*, never about the person: one reviewed maintainer and no
    /// reviewed backup is a risk to the company whoever that person is.
    /// </summary>
    public bool IsSingleMaintainerRisk => ValidatedCover == 1;

    /// <summary>No reviewed cover at all, whether or not anyone has claimed it.</summary>
    public bool HasNoValidatedCover => ValidatedCover == 0;
}

/// <summary>
/// The coverage view plus the caveats a reader needs to interpret it. The
/// design document asks for uncertainty to be "as prominent as results";
/// this is that, as a type rather than as a paragraph in a Razor file.
/// </summary>
public sealed record SkillCoverageReport
{
    public required IReadOnlyList<SkillCoverageRow> Rows { get; init; }

    /// <summary>The date the in-date/overdue split was computed against.</summary>
    public required DateOnly AsOf { get; init; }

    /// <summary>Active staff in the tenant — the denominator for "how much of the team has said anything".</summary>
    public required int StaffInScope { get; init; }

    /// <summary>Active staff with no current assertion of any kind. The silent majority a coverage number hides.</summary>
    public required int StaffWithNoAssertions { get; init; }

    public int SkillsWithoutValidatedCover => Rows.Count(r => r.HasNoValidatedCover);

    public int SingleMaintainerSkills => Rows.Count(r => r.IsSingleMaintainerRisk);
}
