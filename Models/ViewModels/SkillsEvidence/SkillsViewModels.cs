using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Models.ViewModels.SkillsEvidence;

/// <summary>
/// One person's own skills page: their live assertions, the full history
/// behind each, and the skills they could still declare.
/// </summary>
public sealed record MySkillsViewModel
{
    public required IReadOnlyList<SkillAssertionRowViewModel> Current { get; init; }

    /// <summary>Active skills this person has no current assertion for.</summary>
    public required IReadOnlyList<SkillDefinition> Declarable { get; init; }

    public required DateOnly AsOf { get; init; }

    /// <summary>
    /// What the connected sources say about this person, shown to the
    /// person themselves.
    ///
    /// Added in the hardening pass because it was a real asymmetry: a
    /// staff member could see and challenge their declared *skills*, but
    /// evidence collected about them from GitHub and the support desk
    /// appeared only in an admin-run GDPR export. Being able to inspect
    /// what a system says about you is the mitigation that makes the
    /// collection defensible, and it should not require asking an admin
    /// to run a report.
    ///
    /// Null when no repository source is connected or the tenant's plan
    /// does not include one.
    /// </summary>
    public Services.SkillsEvidence.StaffEvidencePortfolio? OwnEvidence { get; init; }

    /// <summary>
    /// Support cases this person is recorded as having resolved or
    /// reviewed. Empty when no desk is connected.
    /// </summary>
    public IReadOnlyList<Models.ServiceOps.SupportCaseParticipant> OwnSupportParticipation { get; init; } = [];

    /// <summary>Whether the viewer may also review other people's records (links to the review queue).</summary>
    public bool CanReview { get; init; }

    /// <summary>
    /// The external accounts approved as this person, so they can see
    /// what their evidence is built from — and tell an admin if one is
    /// not theirs.
    /// </summary>
    public IReadOnlyList<EvidenceActorLink> MappedAccounts => OwnEvidence?.MappedAccounts ?? [];

    public bool HasAnyEvidence =>
        OwnEvidence is { Evidence.Count: > 0 } || OwnSupportParticipation.Count > 0;
}

/// <summary>
/// A live assertion plus the display name of its skill and the superseded
/// rows behind it. The history is shown to the subject in full — they are
/// entitled to see every version of their own record, including the ones a
/// reviewer replaced.
/// </summary>
public sealed record SkillAssertionRowViewModel
{
    public required StaffSkillAssertion Assertion { get; init; }

    public required string SkillName { get; init; }

    public required SkillKind Kind { get; init; }

    /// <summary>Superseded rows for this skill, newest first.</summary>
    public required IReadOnlyList<StaffSkillAssertion> History { get; init; }

    public bool IsReviewOverdue(DateOnly asOf) =>
        Assertion.Status == AssertionStatus.Validated
        && Assertion.ReviewDueOn is not null
        && Assertion.ReviewDueOn.Value < asOf;
}

/// <summary>
/// The reviewer's queue: everything in this tenant waiting on a decision.
/// Carries names, because a reviewer has to know whose record they are
/// deciding — this page needs <c>ViewStaffSkillEvidence</c>, the narrower
/// grant, unlike the coverage page.
/// </summary>
public sealed record SkillReviewQueueViewModel
{
    public required IReadOnlyList<SkillReviewItemViewModel> Items { get; init; }

    public required DateOnly AsOf { get; init; }

    /// <summary>Whether the viewer may validate or reject, rather than only read the queue.</summary>
    public bool CanDecide { get; init; }
}

public sealed record SkillReviewItemViewModel
{
    public required StaffSkillAssertion Assertion { get; init; }

    public required string StaffName { get; init; }

    public required string SkillName { get; init; }

    /// <summary>True when the person is contesting a decision rather than making a fresh claim.</summary>
    public bool IsChallenge => Assertion.Status == AssertionStatus.ChallengeRaised;
}

/// <summary>
/// One person's record as a reviewer sees it: their reviewed skills and,
/// when a repository source is connected, the engineering evidence
/// alongside them.
///
/// The two are placed side by side and joined nowhere else. A reviewer
/// reads "declares Practitioner in C#" next to "authored 14 merged pull
/// requests touching C# files in this window" and draws their own
/// conclusion. The product does not draw it for them: evidence never
/// raises an assertion, and there is no combined score.
/// </summary>
public sealed record StaffSkillPortfolioViewModel
{
    public required Guid StaffKey { get; init; }

    public required string StaffName { get; init; }

    public required string? JobTitle { get; init; }

    public required IReadOnlyList<SkillAssertionRowViewModel> Current { get; init; }

    public required DateOnly AsOf { get; init; }

    /// <summary>
    /// Null when no evidence source is connected, or when the tenant's
    /// plan does not include one — which is different from "connected and
    /// found nothing", and the view says so.
    /// </summary>
    public Services.SkillsEvidence.StaffEvidencePortfolio? Evidence { get; init; }

    /// <summary>Whether the viewer may record a reviewed skill for this person.</summary>
    public bool CanDecide { get; init; }
}
