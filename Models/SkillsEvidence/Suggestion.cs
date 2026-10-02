namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// What a suggestion is proposing. Each maps to something a person could
/// already have done by hand — a suggestion never reaches anywhere the
/// manual route does not.
/// </summary>
public enum SuggestionKind
{
    /// <summary>
    /// "This person has touched C# files repeatedly; would you like to
    /// declare a skill?" Proposes a <see cref="StaffSkillAssertion"/> at
    /// the lowest level, never a proficiency.
    /// </summary>
    SkillTag = 0,

    /// <summary>
    /// "This component has one reviewed maintainer and no approved
    /// backup." Proposes a <see cref="CoverageAction"/>.
    /// </summary>
    CoverageGap = 1,

    /// <summary>
    /// "This support case and this pull request share an issue key."
    /// Proposes a relationship link — never a root cause.
    /// </summary>
    CaseRelationship = 2
}

/// <summary>
/// How confident the rule is, in words rather than a number.
///
/// Deliberately three bands and not a percentage. A percentage implies a
/// calibrated model; these are threshold rules over counted evidence, and
/// "73% confident" would be a fabrication dressed as precision. The
/// bands say what they mean: how much evidence there is, not how likely
/// the suggestion is to be right.
/// </summary>
public enum SuggestionConfidence
{
    /// <summary>Just past the threshold. Worth a look, easily wrong.</summary>
    Weak = 0,

    /// <summary>Comfortably past it, from more than one artefact.</summary>
    Moderate = 1,

    /// <summary>A lot of evidence, over a decent window.</summary>
    Strong = 2
}

public enum SuggestionOutcome
{
    /// <summary>Waiting for a person.</summary>
    Open = 0,

    /// <summary>A person accepted it, and the thing it proposed was created.</summary>
    Accepted = 1,

    /// <summary>
    /// A person said no. Recorded, so the same suggestion is not
    /// re-raised on every sync — a suggestion engine that nags is one
    /// people learn to ignore, and an ignored queue hides the useful
    /// ones.
    /// </summary>
    Dismissed = 2
}

/// <summary>
/// A proposal, with its evidence, waiting for a human decision.
///
/// **A suggestion is never a fact and never becomes one on its own.**
/// The design document's fifth phase asks for proposals with confidence
/// and citations that require human acceptance before they become
/// assertions or causal links, and the shape of this record is where
/// that is enforced: it has an outcome and an acceptor, and nothing
/// reads suggestions as though they were assertions.
///
/// Accepting a <see cref="SuggestionKind.SkillTag"/> creates a
/// *self-declared, Submitted* assertion at
/// <see cref="ProficiencyLevel.Awareness"/> — the weakest thing the
/// domain can express — which then goes through the same review a
/// hand-typed declaration does. It does not create a validated skill,
/// and it cannot raise an existing one.
///
/// Accepting a <see cref="SuggestionKind.CaseRelationship"/> creates a
/// relationship link, never a <see cref="ServiceOps.SupportLinkMethod.ConfirmedRootCause"/>.
/// </summary>
public sealed record Suggestion
{
    public required Guid SuggestionKey { get; init; }

    public required Guid TenantId { get; init; }

    public required SuggestionKind Kind { get; init; }

    /// <summary>
    /// Who it is about, for a <see cref="SuggestionKind.SkillTag"/>.
    /// Null for suggestions about a component or a case, which are not
    /// about a person at all.
    /// </summary>
    public Guid? SubjectStaffKey { get; init; }

    /// <summary>The skill key, component key or case id the suggestion concerns.</summary>
    public required string SubjectKey { get; init; }

    /// <summary>For a case relationship, the artefact proposed as related.</summary>
    public string? RelatedKey { get; init; }

    public required SuggestionConfidence Confidence { get; init; }

    /// <summary>
    /// Why the rule fired, in a sentence somebody can disagree with:
    /// "11 commits touching C# files in 4 repositories since March".
    /// </summary>
    public required string Rationale { get; init; }

    /// <summary>
    /// What it is based on — the citations. Source URLs where there are
    /// any, so a reviewer can check the claim rather than trust it.
    /// </summary>
    public IReadOnlyList<string> Citations { get; init; } = [];

    /// <summary>How many artefacts the rule counted. The denominator behind the confidence band.</summary>
    public required int EvidenceCount { get; init; }

    /// <summary>The window the evidence was drawn from. A count with no period means nothing.</summary>
    public required DateOnly ObservedFrom { get; init; }

    public required DateOnly ObservedTo { get; init; }

    public required SuggestionOutcome Outcome { get; init; }

    public Guid? DecidedByStaffKey { get; init; }

    public DateTime? DecidedAtUtc { get; init; }

    /// <summary>Why it was dismissed. Optional — a person may simply disagree.</summary>
    public string? DecisionNote { get; init; }

    public required DateTime RaisedAtUtc { get; init; }

    public bool IsOpen => Outcome == SuggestionOutcome.Open;

    /// <summary>
    /// Whether this suggestion is about an identified person. Used to
    /// keep person-level suggestions behind the narrower capability,
    /// exactly as the evidence they are derived from is.
    /// </summary>
    public bool IsAboutAPerson => SubjectStaffKey is not null;
}

/// <summary>
/// The thresholds the suggestion rules fire on.
///
/// Public, named and few, because a customer will ask "why did it
/// suggest that?" and "because eleven is more than five" is an answer.
/// A tuned model would not be.
/// </summary>
public static class SuggestionThresholds
{
    /// <summary>
    /// Distinct artefacts carrying a language hint before a skill tag is
    /// proposed. Three is deliberately low — the proposal is only for an
    /// Awareness-level self-declaration that still needs review, so the
    /// cost of a weak suggestion is one dismissal.
    /// </summary>
    public const int MinimumArtefactsForSkillTag = 3;

    public const int ModerateArtefacts = 8;
    public const int StrongArtefacts = 20;

    /// <summary>The window skill-tag suggestions are drawn from.</summary>
    public const int ObservationWindowDays = 180;

    /// <summary>Citations kept per suggestion. Enough to check, not so many the page is a log.</summary>
    public const int MaxCitations = 5;

    public static SuggestionConfidence BandFor(int evidenceCount) => evidenceCount switch
    {
        >= StrongArtefacts => SuggestionConfidence.Strong,
        >= ModerateArtefacts => SuggestionConfidence.Moderate,
        _ => SuggestionConfidence.Weak
    };
}
