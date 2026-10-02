using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The assertion lifecycle: declare, validate, reject, challenge, withdraw.
/// Every one of these appends a row and supersedes the previous one, so a
/// caller cannot accidentally lose a prior review.
///
/// The service takes the acting staff key explicitly rather than reading it
/// from the current member. Two reasons: it keeps the rules unit-testable
/// without a MemberManager (which this codebase cannot fake — see
/// docs/tenancy.md), and it forces the controller to be explicit about
/// *whose* action it is, which is the difference between a self-declaration
/// and a reviewer recording something on someone's behalf.
///
/// Authorization is the controller's job, not this service's. What this
/// enforces is the narrower domain rule that a reviewer may not validate
/// their own assertion.
/// </summary>
public interface ISkillAssertionService
{
    /// <summary>
    /// A staff member claims a skill about themselves, or re-claims one
    /// they withdrew. Supersedes any current assertion for that skill,
    /// including a validated one — re-declaring at a new level is how you
    /// ask for a re-review, and the old validated row stays in history.
    /// </summary>
    Task<StaffSkillAssertion> DeclareAsync(
        Guid staffKey, string skillKey, ProficiencyLevel proficiency, string? evidenceNote,
        Guid tenantId, int? actorMemberId);

    /// <summary>
    /// A reviewer records a skill on someone else's behalf, already
    /// validated. <see cref="AssertionOrigin.ReviewerRecorded"/>, so a
    /// reader can tell it apart from one the employee agreed to.
    /// </summary>
    Task<StaffSkillAssertion> RecordForStaffAsync(
        Guid subjectStaffKey, Guid reviewerStaffKey, string skillKey, ProficiencyLevel proficiency,
        string reviewNote, DateOnly? reviewDueOn, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// A reviewer agrees, optionally at a different level from the one
    /// claimed. <paramref name="reviewNote"/> is required — an unexplained
    /// downgrade is the thing an employee will most want to contest.
    /// </summary>
    Task<StaffSkillAssertion> ValidateAsync(
        Guid assertionKey, Guid reviewerStaffKey, ProficiencyLevel proficiency, string reviewNote,
        DateOnly? reviewDueOn, Guid tenantId, int? actorMemberId);

    /// <summary>A reviewer disagrees. <paramref name="reviewNote"/> is required.</summary>
    Task<StaffSkillAssertion> RejectAsync(
        Guid assertionKey, Guid reviewerStaffKey, string reviewNote, Guid tenantId, int? actorMemberId);

    /// <summary>
    /// The correction flow: the staff member disputes what is on their
    /// record and says what they think it should be. Goes back into the
    /// review queue and stops counting as cover while contested.
    /// </summary>
    Task<StaffSkillAssertion> ChallengeAsync(
        Guid assertionKey, Guid staffKey, ProficiencyLevel claimedProficiency, string reason,
        Guid tenantId, int? actorMemberId);

    /// <summary>The staff member no longer claims this skill.</summary>
    Task<StaffSkillAssertion> WithdrawAsync(
        Guid assertionKey, Guid staffKey, Guid tenantId, int? actorMemberId);

    // ---- Taxonomy ----

    Task<SkillDefinition> CreateSkillAsync(
        string rawKeyOrName, string name, SkillKind kind, string? description, Guid tenantId, int? actorMemberId);

    Task<SkillDefinition> SetSkillActiveAsync(string skillKey, bool isActive, Guid tenantId, int? actorMemberId);
}
