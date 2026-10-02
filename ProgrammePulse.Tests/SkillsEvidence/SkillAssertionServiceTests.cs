using System.Text.Json;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The assertion lifecycle: declare, validate, reject, challenge, withdraw.
///
/// The invariant these are really defending is that a review is never lost.
/// Every transition appends; nothing is updated in place except the
/// supersession stamp. A test that only checked the *current* row would
/// pass against an implementation that overwrote history, so several of
/// these assert on the history as well.
/// </summary>
public class SkillAssertionServiceTests
{
    private const string Csharp = "csharp";

    [Fact]
    public async Task Declaring_a_skill_records_a_self_declared_claim_awaiting_review()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var assertion = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, "two years on the sync engine",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        Assert.Equal(AssertionStatus.Submitted, assertion.Status);
        Assert.Equal(AssertionOrigin.SelfDeclared, assertion.Origin);
        Assert.Equal(ProficiencyLevel.Working, assertion.Proficiency);
        Assert.Equal("two years on the sync engine", assertion.EvidenceNote);
        Assert.True(assertion.IsCurrent);
        // Nothing is validated on declaration — a claim is not evidence.
        Assert.Null(assertion.ReviewerStaffKey);
        Assert.Null(assertion.ReviewDueOn);
    }

    [Fact]
    public async Task Validating_supersedes_the_claim_and_keeps_it_in_history()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Practitioner, "shipped the mapper",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        ctx.Time.Advance(TimeSpan.FromDays(3));

        var validated = await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Practitioner, "agreed, I reviewed that work",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        Assert.Equal(AssertionStatus.Validated, validated.Status);
        Assert.Equal(ctx.Sarah.StaffKey, validated.ReviewerStaffKey);
        Assert.Equal("agreed, I reviewed that work", validated.ReviewNote);
        Assert.Equal(declared.AssertionKey, validated.SupersedesAssertionKey);

        var history = await ctx.Repository.GetHistoryForStaffAsync(ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA);
        Assert.Equal(2, history.Count);
        Assert.Single(history, a => a.IsCurrent);
        // The original claim is still readable, stamped with when it stopped being current.
        var original = history.Single(a => a.AssertionKey == declared.AssertionKey);
        Assert.False(original.IsCurrent);
        Assert.Equal(ctx.Time.Now.UtcDateTime, original.SupersededAtUtc);
    }

    [Fact]
    public async Task Validating_defaults_the_review_date_to_the_rubric_interval()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var validated = await ctx.ValidatedAsync(
            ctx.Alex, Csharp, ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        Assert.Equal(
            ProficiencyRubric.DefaultReviewDue(ctx.Time.Now.UtcDateTime),
            validated.ReviewDueOn);
    }

    [Fact]
    public async Task A_reviewer_may_validate_at_a_lower_level_than_was_claimed()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Lead, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        var validated = await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working,
            "you have done the routine work but not set the approach", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        Assert.Equal(ProficiencyLevel.Working, validated.Proficiency);
        // And the claim that was made is still on the record, which is what
        // the person needs in order to contest the downgrade.
        var history = await ctx.Repository.GetHistoryForStaffAsync(ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA);
        Assert.Contains(history, a => a.Proficiency == ProficiencyLevel.Lead);
    }

    [Fact]
    public async Task Validating_without_a_rationale_is_refused()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "   ", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId));
    }

    [Fact]
    public async Task Rejecting_without_a_rationale_is_refused()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.RejectAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, "", SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId));
    }

    [Fact]
    public async Task Nobody_can_validate_or_reject_their_own_assertion()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Sarah.StaffKey, Csharp, ProficiencyLevel.Lead, null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        // Sarah holds ReviewStaffSkills. The capability is not the point —
        // self-review is a domain rule, enforced below the authorization layer.
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Lead, "I am great", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId));

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.RejectAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, "on reflection, no",
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId));
    }

    [Fact]
    public async Task Recording_a_skill_for_someone_marks_it_reviewer_recorded_not_self_declared()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var recorded = await ctx.Service.RecordForStaffAsync(
            ctx.Alex.StaffKey, ctx.Sarah.StaffKey, Csharp, ProficiencyLevel.Practitioner,
            "inherited the component from a leaver", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        Assert.Equal(AssertionOrigin.ReviewerRecorded, recorded.Origin);
        Assert.Equal(AssertionStatus.Validated, recorded.Status);
        Assert.Equal(ctx.Sarah.StaffKey, recorded.RecordedByStaffKey);
        Assert.Equal(ctx.Alex.StaffKey, recorded.StaffKey);
    }

    [Fact]
    public async Task Validating_carries_the_self_declared_origin_forward()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var validated = await ctx.ValidatedAsync(
            ctx.Alex, Csharp, ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        // "Validated" describes the review, not who first claimed it — a
        // reader must still be able to tell that Alex said this about
        // themselves.
        Assert.Equal(AssertionOrigin.SelfDeclared, validated.Origin);
    }

    // ---- The correction flow ----

    [Fact]
    public async Task A_staff_member_can_challenge_a_validated_level_and_it_returns_to_the_queue()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Practitioner, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        var validated = await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Awareness, "not seen enough",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        var challenge = await ctx.Service.ChallengeAsync(
            validated.AssertionKey, ctx.Alex.StaffKey, ProficiencyLevel.Practitioner,
            "I led the migration in March, here is the PR",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        Assert.Equal(AssertionStatus.ChallengeRaised, challenge.Status);
        Assert.Equal(ProficiencyLevel.Practitioner, challenge.Proficiency);
        // The decision being disputed is carried forward, so the reviewer
        // sees what they said next to the objection.
        Assert.Equal("not seen enough", challenge.ReviewNote);
        Assert.Equal(ctx.Sarah.StaffKey, challenge.ReviewerStaffKey);
    }

    [Fact]
    public async Task A_challenge_cannot_be_raised_against_someone_elses_record()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var validated = await ctx.ValidatedAsync(
            ctx.Alex, Csharp, ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        // Nia is in the same tenant and is a real person; the record still
        // is not hers, so it reads as not found rather than as forbidden.
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.Service.ChallengeAsync(
            validated.AssertionKey, ctx.Nia.StaffKey, ProficiencyLevel.Lead, "actually that is me",
            SkillsEvidenceTestContext.TenantA, ctx.Nia.MemberId));
    }

    [Fact]
    public async Task A_claim_already_waiting_for_review_cannot_also_be_challenged()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.ChallengeAsync(
            declared.AssertionKey, ctx.Alex.StaffKey, ProficiencyLevel.Lead, "changed my mind",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId));
    }

    [Fact]
    public async Task Withdrawing_then_redeclaring_keeps_the_whole_chain()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var validated = await ctx.ValidatedAsync(
            ctx.Alex, Csharp, ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var withdrawn = await ctx.Service.WithdrawAsync(
            validated.AssertionKey, ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        Assert.Equal(AssertionStatus.Withdrawn, withdrawn.Status);

        var redeclared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Lead, "picked it back up",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        Assert.Equal(AssertionStatus.Submitted, redeclared.Status);

        var history = await ctx.Repository.GetHistoryForStaffAsync(ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA);
        Assert.Equal(4, history.Count);                 // declared, validated, withdrawn, redeclared
        Assert.Single(history, a => a.IsCurrent);       // exactly one live row throughout
    }

    [Fact]
    public async Task Redeclaring_a_validated_skill_supersedes_it_rather_than_creating_a_second_live_row()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        await ctx.ValidatedAsync(ctx.Alex, Csharp, ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);
        await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Practitioner, "moved up",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        var current = await ctx.Repository.GetCurrentForStaffAsync(ctx.Alex.StaffKey, SkillsEvidenceTestContext.TenantA);
        Assert.Single(current);
        Assert.Equal(AssertionStatus.Submitted, current[0].Status);
    }

    // ---- Cross-tenant ----

    [Fact]
    public async Task A_skill_key_belonging_to_another_tenant_cannot_be_declared_against()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync("tenant-b-only", SkillsEvidenceTestContext.TenantB);

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, "tenant-b-only", ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId));
    }

    [Fact]
    public async Task An_assertion_in_another_tenant_cannot_be_validated_from_this_one()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantB);

        var theirs = await ctx.Service.DeclareAsync(
            ctx.Rhian.StaffKey, Csharp, ProficiencyLevel.Lead, null,
            SkillsEvidenceTestContext.TenantB, ctx.Rhian.MemberId);

        // Sarah is a legitimate reviewer — in the wrong tenant. The key is
        // real, and still has to read as not found.
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.Service.ValidateAsync(
            theirs.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Awareness, "downgrading a stranger",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId));
    }

    [Fact]
    public async Task Two_tenants_may_track_the_same_skill_key_independently()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync("billing", SkillsEvidenceTestContext.TenantA, SkillKind.Component);
        await ctx.AddSkillAsync("billing", SkillsEvidenceTestContext.TenantB, SkillKind.Component);

        await ctx.ValidatedAsync(ctx.Alex, "billing", ProficiencyLevel.Lead, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var tenantB = await ctx.Repository.GetCurrentForTenantAsync(SkillsEvidenceTestContext.TenantB);
        Assert.Empty(tenantB);
    }

    [Fact]
    public async Task A_duplicate_skill_key_within_one_tenant_is_refused()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync("csharp", SkillsEvidenceTestContext.TenantA);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.CreateSkillAsync("csharp", "C#", SkillKind.Language, null, SkillsEvidenceTestContext.TenantA, 1));
    }

    [Fact]
    public async Task Two_spellings_that_fold_to_the_same_key_are_caught_as_a_duplicate()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync("incident-command", SkillsEvidenceTestContext.TenantA, SkillKind.Practice);

        // "Incident Command" normalises to incident-command, which is the
        // whole reason keys are normalised: two spellings must not split
        // one skill's coverage count in half.
        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.CreateSkillAsync("Incident Command", "Incident Command", SkillKind.Practice, null, SkillsEvidenceTestContext.TenantA, 1));

        Assert.Contains("incident-command", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Different_skills_whose_names_merely_look_alike_remain_separate()
    {
        var ctx = new SkillsEvidenceTestContext();

        // "csharp" and "C Sharp" do *not* collide — the second folds to
        // "c-sharp". That is deliberate: normalisation removes spelling
        // noise, it does not guess that two keys mean the same thing.
        // Aliasing is a reviewed act, per the module's data contract.
        var first = await ctx.Service.CreateSkillAsync("csharp", "C#", SkillKind.Language, null, SkillsEvidenceTestContext.TenantA, 1);
        var second = await ctx.Service.CreateSkillAsync("C Sharp", "C Sharp (legacy tag)", SkillKind.Language, null, SkillsEvidenceTestContext.TenantA, 1);

        Assert.Equal("csharp", first.SkillKey);
        Assert.Equal("c-sharp", second.SkillKey);
    }

    [Fact]
    public async Task A_retired_skill_takes_no_new_assertions()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.Service.SetSkillActiveAsync(Csharp, false, SkillsEvidenceTestContext.TenantA, 1);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId));
    }

    [Fact]
    public async Task A_retired_skill_can_still_have_an_outstanding_assertion_decided()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        await ctx.Service.SetSkillActiveAsync(Csharp, false, SkillsEvidenceTestContext.TenantA, 1);

        // Retiring a skill must not strand a claim in the queue with no way
        // to close it out.
        var validated = await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "closing this out",
            null, SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        Assert.Equal(AssertionStatus.Validated, validated.Status);
    }

    [Fact]
    public async Task Acting_on_a_superseded_row_is_refused_rather_than_silently_applied()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, null,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "fine", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        // Two reviewers with the queue open in two tabs: the second one's
        // key is now stale, and must not overwrite the first decision.
        var stale = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Nia.StaffKey, ProficiencyLevel.Lead, "actually a Lead", null,
            SkillsEvidenceTestContext.TenantA, ctx.Nia.MemberId));

        Assert.Contains("older version", stale.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Audit ----

    [Fact]
    public async Task Every_transition_writes_one_audit_entry_naming_the_actor()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, "note", SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        var validated = await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, "agreed", null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);
        await ctx.Service.ChallengeAsync(
            validated.AssertionKey, ctx.Alex.StaffKey, ProficiencyLevel.Lead, "should be higher",
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);

        var actions = ctx.AuditLog.Entries
            .Where(e => e.EntityType == SkillsEvidenceAuditAction.EntityTypeAssertion)
            .Select(e => e.Action)
            .ToList();

        Assert.Equal(
            [SkillsEvidenceAuditAction.AssertionDeclared,
             SkillsEvidenceAuditAction.AssertionValidated,
             SkillsEvidenceAuditAction.AssertionChallenged],
            actions);

        var review = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.AssertionValidated);
        Assert.Equal(ctx.Sarah.MemberId, review.ActorMemberId);
        Assert.Equal(SkillsEvidenceTestContext.TenantA, review.TenantId);
    }

    [Fact]
    public async Task Creating_and_retiring_a_skill_is_audited()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);
        await ctx.Service.SetSkillActiveAsync(Csharp, false, SkillsEvidenceTestContext.TenantA, 9);
        await ctx.Service.SetSkillActiveAsync(Csharp, true, SkillsEvidenceTestContext.TenantA, 9);

        var actions = ctx.AuditLog.Entries
            .Where(e => e.EntityType == SkillsEvidenceAuditAction.EntityTypeSkill)
            .Select(e => e.Action)
            .ToList();

        Assert.Equal(
            [SkillsEvidenceAuditAction.SkillCreated,
             SkillsEvidenceAuditAction.SkillRetired,
             SkillsEvidenceAuditAction.SkillReinstated],
            actions);
    }

    /// <summary>
    /// The audit log outlives the subject's erasure, so anything written to
    /// it outlives the erasure too. The free-text notes are the revealing
    /// part of an assertion and belong only on the row that gets deleted —
    /// if this test fails, erasure has started leaving personal data behind.
    /// </summary>
    [Fact]
    public async Task Audit_detail_never_carries_the_free_text_notes()
    {
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync(Csharp, SkillsEvidenceTestContext.TenantA);

        const string ownWords = "I was signed off sick when the incident happened";
        const string reviewerWords = "discussed at their probation meeting";

        var declared = await ctx.Service.DeclareAsync(
            ctx.Alex.StaffKey, Csharp, ProficiencyLevel.Working, ownWords,
            SkillsEvidenceTestContext.TenantA, ctx.Alex.MemberId);
        await ctx.Service.ValidateAsync(
            declared.AssertionKey, ctx.Sarah.StaffKey, ProficiencyLevel.Working, reviewerWords, null,
            SkillsEvidenceTestContext.TenantA, ctx.Sarah.MemberId);

        foreach (var entry in ctx.AuditLog.Entries)
        {
            Assert.DoesNotContain(ownWords, entry.DetailJson ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(reviewerWords, entry.DetailJson ?? string.Empty, StringComparison.Ordinal);
        }

        // ...while still recording enough to hold someone to the decision.
        var review = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.AssertionValidated);
        var detail = JsonDocument.Parse(review.DetailJson!).RootElement;
        Assert.Equal(ctx.Sarah.StaffKey.ToString(), detail.GetProperty("reviewerStaffKey").GetString());
        Assert.Equal("Working", detail.GetProperty("proficiency").GetString());
        Assert.Equal("Validated", detail.GetProperty("status").GetString());
    }
}
