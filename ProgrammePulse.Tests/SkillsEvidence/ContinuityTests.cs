using System.Reflection;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The continuity plan: declared ownership, approved cover, and the
/// Platinum actions that follow.
///
/// The rule underneath most of these is that a plan is a *decision*, not
/// a derived number. Nothing infers an owner, nothing auto-raises an
/// action, and closing one requires saying what happened.
/// </summary>
public class ContinuityTests
{
    private sealed class Context
    {
        public FakeContinuityRepository Repository { get; } = new();
        public FakeSkillsEvidenceRepository Skills { get; } = new();
        public FakeSkillsEvidenceAuditLogRepository AuditLog { get; } = new();
        public ProgrammeOps.FakeStaffRepository StaffRepository { get; } = new();
        public FixedTimeProvider Time { get; } = new(SkillsEvidenceTestContext.Start);

        public ContinuityService Service { get; }
        public KeyPersonCoverageQueryService Coverage { get; }
        public ContinuityDataParticipant Participant { get; }

        public StaffProfile Alex { get; }
        public StaffProfile Sarah { get; }
        public StaffProfile Nia { get; }

        public Context()
        {
            Service = new ContinuityService(Repository, AuditLog, Time);
            Coverage = new KeyPersonCoverageQueryService(Repository, Skills, StaffRepository, Time);
            Participant = new ContinuityDataParticipant(Repository, AuditLog, StaffRepository);

            Alex = AddStaff("Alex Morgan", 101);
            Sarah = AddStaff("Sarah Evans", 102);
            Nia = AddStaff("Nia Roberts", 103);
        }

        public StaffProfile AddStaff(string name, int memberId, bool isActive = true)
        {
            var staff = new StaffProfile
            {
                StaffKey = Guid.NewGuid(),
                MemberId = memberId,
                FullName = name,
                Email = $"{memberId}@acme.test",
                IsActive = isActive,
                TenantId = SkillsEvidenceTestContext.TenantA,
                CreatedAtUtc = SkillsEvidenceTestContext.Start.UtcDateTime,
                UpdatedAtUtc = SkillsEvidenceTestContext.Start.UtcDateTime
            };

            StaffRepository.Staff.Add(staff);
            return staff;
        }

        public void Deactivate(StaffProfile staff)
        {
            var index = StaffRepository.Staff.FindIndex(s => s.StaffKey == staff.StaffKey);
            StaffRepository.Staff[index] = staff with { IsActive = false };
        }

        public Task<ComponentOwnership> DeclareAsync(string name, Guid? owner = null) =>
            Service.DeclareComponentAsync(name, name, null, owner, Sarah.StaffKey, SkillsEvidenceTestContext.TenantA, 1);
    }

    private static Guid TenantA => SkillsEvidenceTestContext.TenantA;

    [Fact]
    public async Task Declaring_a_component_marks_it_reviewed_today()
    {
        var ctx = new Context();

        var component = await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        Assert.Equal("billing", component.ComponentKey);
        Assert.Equal(ctx.Alex.StaffKey, component.OwnerStaffKey);
        // Declaring it *is* reviewing it — the manager is saying this is
        // true today, which is what the review date records.
        Assert.Equal(ctx.Time.Now.UtcDateTime, component.LastReviewedAtUtc);
        Assert.Equal(ctx.Sarah.StaffKey, component.ReviewedByStaffKey);
        Assert.True(component.IsReviewCurrentOn(DateOnly.FromDateTime(ctx.Time.Now.UtcDateTime)));
    }

    [Fact]
    public async Task An_ownership_record_goes_stale_on_the_same_clock_as_a_skill()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        ctx.Time.Advance(TimeSpan.FromDays(400));
        var report = await ctx.Coverage.BuildAsync(TenantA);

        Assert.False(report.Components.Single().IsReviewCurrent);
        Assert.Equal(1, report.ComponentsWithStaleReview);
        // Every component stale means the map is not worth publishing as
        // a risk view.
        Assert.False(report.IsPublishable);
    }

    [Fact]
    public async Task Confirming_moves_the_review_date_without_changing_the_map()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        ctx.Time.Advance(TimeSpan.FromDays(400));

        await ctx.Service.ConfirmComponentAsync("billing", ctx.Nia.StaffKey, TenantA, 1);

        var component = ctx.Repository.Components.Single();
        Assert.Equal(ctx.Alex.StaffKey, component.OwnerStaffKey);
        Assert.Equal(ctx.Nia.StaffKey, component.ReviewedByStaffKey);
        Assert.True((await ctx.Coverage.BuildAsync(TenantA)).Components.Single().IsReviewCurrent);
    }

    [Fact]
    public async Task An_unowned_component_is_the_most_exposed_state_and_sorts_first()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("owned", ctx.Alex.StaffKey);
        await ctx.DeclareAsync("orphan");

        var report = await ctx.Coverage.BuildAsync(TenantA);

        Assert.Equal(1, report.UnownedComponents);
        Assert.Equal("orphan", report.Components[0].ComponentKey);
        Assert.True(report.Components[0].HasNoOwner);
    }

    [Fact]
    public async Task One_owner_with_no_approved_cover_is_a_single_person_exposure()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        var row = (await ctx.Coverage.BuildAsync(TenantA)).Components.Single();

        Assert.True(row.IsSinglePersonExposure);
        Assert.True(row.HasNoApprovedBackup);
    }

    [Fact]
    public async Task Approving_cover_clears_the_exposure()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, "shadowed the last release", ctx.Nia.StaffKey, TenantA, 1);

        var row = (await ctx.Coverage.BuildAsync(TenantA)).Components.Single();
        Assert.False(row.IsSinglePersonExposure);
        Assert.Single(row.ApprovedBackups);
        Assert.Equal("Sarah Evans", row.ApprovedBackups[0].Name);
    }

    [Fact]
    public async Task The_owner_cannot_be_their_own_backup()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.ApproveBackupAsync("billing", ctx.Alex.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1));

        Assert.Contains("somebody else", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_person_cannot_be_approved_twice_for_one_component()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1));
    }

    [Fact]
    public async Task A_backup_who_has_left_stops_counting_as_cover()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1);

        ctx.Deactivate(ctx.Sarah);

        // Counting a leaver as cover is the false reassurance this view
        // exists to prevent.
        var row = (await ctx.Coverage.BuildAsync(TenantA)).Components.Single();
        Assert.Empty(row.ApprovedBackups);
        Assert.True(row.IsSinglePersonExposure);
    }

    [Fact]
    public async Task An_owner_who_has_left_leaves_the_component_unowned()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        ctx.Deactivate(ctx.Alex);

        Assert.True((await ctx.Coverage.BuildAsync(TenantA)).Components.Single().HasNoOwner);
    }

    [Fact]
    public async Task Approved_cover_holding_no_validated_skill_is_surfaced_rather_than_resolved()
    {
        var ctx = new Context();
        // The vocabularies line up: a component-kind skill with the same key.
        ctx.Skills.Skills.Add(new SkillDefinition
        {
            SkillDefinitionKey = Guid.NewGuid(),
            TenantId = TenantA,
            SkillKey = "billing",
            Name = "Billing",
            Kind = SkillKind.Component,
            TaxonomyVersion = SkillTaxonomy.CurrentVersion,
            CreatedAtUtc = ctx.Time.Now.UtcDateTime,
            UpdatedAtUtc = ctx.Time.Now.UtcDateTime
        });

        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1);

        var row = (await ctx.Coverage.BuildAsync(TenantA)).Components.Single();

        Assert.Equal(0, row.ValidatedSkillCover);
        // The plan may be fine and the skills record stale, or the
        // reverse. Saying which is not the product's call.
        Assert.True(row.BackupsLackValidatedSkill);
    }

    [Fact]
    public async Task An_unaligned_skill_vocabulary_reads_as_not_tracked_rather_than_zero()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        var report = await ctx.Coverage.BuildAsync(TenantA);

        // Null, not 0 — "we do not track a skill for this" is a
        // different statement from "nobody holds it".
        Assert.Null(report.Components.Single().ValidatedSkillCover);
        Assert.True(report.SkillVocabularyUnaligned);
    }

    // ---- Actions (Platinum) ----

    [Fact]
    public async Task An_action_needs_an_owner_and_a_rationale()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, Guid.Empty, "no owner", null, null, ctx.Sarah.StaffKey, TenantA, 1));

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Nia.StaffKey, "   ", null, null, ctx.Sarah.StaffKey, TenantA, 1));
    }

    [Fact]
    public async Task Closing_an_action_requires_saying_what_happened()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Nia.StaffKey, "one person, no cover",
            "1 validated maintainer, last reviewed March", null, ctx.Sarah.StaffKey, TenantA, 1);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.CloseActionAsync(
            action.ActionKey, CoverageActionOutcome.Completed, "  ", ctx.Sarah.StaffKey, TenantA, 1));

        Assert.Contains("what was actually done", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Abandoning_an_action_also_requires_a_reason()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Training, ctx.Nia.StaffKey, "needs training", null, null, ctx.Sarah.StaffKey, TenantA, 1);

        // A decision that quietly evaporates is worse than one never made.
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.CloseActionAsync(
            action.ActionKey, CoverageActionOutcome.Abandoned, "", ctx.Sarah.StaffKey, TenantA, 1));
    }

    [Fact]
    public async Task A_closed_action_records_its_outcome_and_cannot_be_closed_twice()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.DocumentRunbook, ctx.Nia.StaffKey, "no runbook", null, null, ctx.Sarah.StaffKey, TenantA, 1);

        var closed = await ctx.Service.CloseActionAsync(
            action.ActionKey, CoverageActionOutcome.Completed, "runbook written and reviewed", ctx.Sarah.StaffKey, TenantA, 1);

        Assert.Equal(CoverageActionOutcome.Completed, closed.Outcome);
        Assert.Equal("runbook written and reviewed", closed.OutcomeNote);
        Assert.Equal(ctx.Sarah.StaffKey, closed.ClosedByStaffKey);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.CloseActionAsync(
            action.ActionKey, CoverageActionOutcome.Completed, "again", ctx.Sarah.StaffKey, TenantA, 1));
    }

    [Fact]
    public async Task An_overdue_action_is_reported_as_overdue()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Nia.StaffKey, "one person",
            null, DateOnly.FromDateTime(ctx.Time.Now.UtcDateTime).AddDays(7), ctx.Sarah.StaffKey, TenantA, 1);

        ctx.Time.Advance(TimeSpan.FromDays(30));
        var report = await ctx.Coverage.BuildAsync(TenantA);

        Assert.Equal(1, report.OpenActions);
        Assert.Equal(1, report.OverdueActions);
    }

    [Fact]
    public async Task An_action_against_another_tenants_component_is_refused()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Nia.StaffKey, "reaching across",
            null, null, ctx.Sarah.StaffKey, SkillsEvidenceTestContext.TenantB, 1));
    }

    [Fact]
    public async Task Every_continuity_change_is_audited()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, null, ctx.Nia.StaffKey, TenantA, 1);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Nia.StaffKey, "one person", null, null, ctx.Sarah.StaffKey, TenantA, 1);
        await ctx.Service.CloseActionAsync(action.ActionKey, CoverageActionOutcome.Completed, "paired for a sprint", ctx.Sarah.StaffKey, TenantA, 1);

        var actions = ctx.AuditLog.Entries.Select(e => e.Action).ToList();
        Assert.Contains(SkillsEvidenceAuditAction.ComponentDeclared, actions);
        Assert.Contains(SkillsEvidenceAuditAction.BackupApproved, actions);
        Assert.Contains(SkillsEvidenceAuditAction.CoverageActionRaised, actions);
        Assert.Contains(SkillsEvidenceAuditAction.CoverageActionClosed, actions);
    }

    [Fact]
    public async Task The_coverage_view_never_includes_another_tenants_components()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        ctx.Repository.Components.Add(new ComponentOwnership
        {
            OwnershipKey = Guid.NewGuid(),
            TenantId = SkillsEvidenceTestContext.TenantB,
            ComponentKey = "their-secret",
            DisplayName = "Their secret",
            CreatedAtUtc = ctx.Time.Now.UtcDateTime,
            UpdatedAtUtc = ctx.Time.Now.UtcDateTime
        });

        var report = await ctx.Coverage.BuildAsync(TenantA);
        Assert.Single(report.Components);
        Assert.Equal("billing", report.Components[0].ComponentKey);
    }

    // ---- GDPR ----

    [Fact]
    public async Task The_export_lists_what_the_subject_owns_and_covers()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Sarah.StaffKey, "shadowed a release", ctx.Nia.StaffKey, TenantA, 1);
        await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, ctx.Sarah.StaffKey, "one person", null, null, ctx.Nia.StaffKey, TenantA, 1);

        var ownerRows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);
        var backupRows = await ctx.Participant.ExportAsync(ctx.Sarah.StaffKey);

        Assert.Contains(ownerRows, r => r.Summary.Contains("owner", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(backupRows, r => r.Summary.Contains("backup cover", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(backupRows, r => r.Summary.Contains("Pair action", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Erasure_leaves_the_component_unowned_rather_than_deleting_it_from_the_register()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Alex.StaffKey);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        // Deleting the component would remove it from the risk register
        // entirely — which is precisely the exposure the view exists to
        // find, not a way to resolve it.
        var report = await ctx.Coverage.BuildAsync(TenantA);
        Assert.Single(report.Components);
        Assert.True(report.Components[0].HasNoOwner);
        Assert.Equal(1, report.UnownedComponents);
    }

    [Fact]
    public async Task Erasure_removes_the_subjects_backup_approvals_and_unassigns_their_actions()
    {
        var ctx = new Context();
        await ctx.DeclareAsync("billing", ctx.Nia.StaffKey);
        await ctx.Service.ApproveBackupAsync("billing", ctx.Alex.StaffKey, null, ctx.Sarah.StaffKey, TenantA, 1);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Training, ctx.Alex.StaffKey, "needs training", null, null, ctx.Sarah.StaffKey, TenantA, 1);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        // An approved backup who has gone is not cover.
        Assert.Empty(ctx.Repository.Backups);
        // The action keeps its rationale and surfaces as needing an owner.
        var remaining = ctx.Repository.Actions.Single(a => a.ActionKey == action.ActionKey);
        Assert.Null(remaining.OwnerStaffKey);
        Assert.True(remaining.NeedsReassignment);
        Assert.Equal("needs training", remaining.Rationale);

        Assert.Contains(ctx.AuditLog.Entries, e => e.Action == SkillsEvidenceAuditAction.ContinuityDetachedForSubject);
    }

    // ---- Structural ----

    /// <summary>
    /// The design document rules out automatic staffing and employment
    /// decisions. Giving the action type nowhere to express one is what
    /// makes that enforceable rather than aspirational.
    /// </summary>
    [Fact]
    public void No_action_type_can_express_an_employment_decision()
    {
        string[] forbidden = ["reassign", "replace", "performance", "dismiss", "demote", "promote", "rank"];

        foreach (var name in Enum.GetNames<CoverageActionType>())
        {
            Assert.DoesNotContain(forbidden, f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(
            new[] { "DocumentRunbook", "Investigate", "NominateBackup", "Pair", "Training" },
            Enum.GetNames<CoverageActionType>().Order().ToArray());
    }

    /// <summary>
    /// Consent is deliberately not an option: the ICO's position is that
    /// it is rarely valid in an employment context, and offering it would
    /// invite customers to pick the basis that is easiest to defend in a
    /// meeting and hardest to defend afterwards.
    /// </summary>
    [Fact]
    public void Consent_is_not_offered_as_a_lawful_basis() =>
        Assert.DoesNotContain(
            Enum.GetNames<EvidenceLawfulBasis>(),
            name => name.Contains("consent", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void The_continuity_service_cannot_reach_a_rate_or_a_contract()
    {
        var dependencies = typeof(ContinuityService).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType.Name).ToArray();

        Assert.DoesNotContain(dependencies, name => name.Contains("Rate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dependencies, name => name.Contains("Contract", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Managing_the_plan_is_a_delivery_grant_and_recording_the_basis_is_not()
    {
        // The delivery manager plans cover; only an Admin may unblock
        // collection of their own team's activity.
        Assert.True(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ManageContinuityPlan));
        Assert.True(RoleCapabilities.Has([StaffRole.Admin], Capability.ManageContinuityPlan));
        Assert.False(RoleCapabilities.Has([StaffRole.TeamLead], Capability.RecordProcessingDecision));
        Assert.True(RoleCapabilities.Has([StaffRole.Admin], Capability.RecordProcessingDecision));

        foreach (var role in new[] { StaffRole.Staff, StaffRole.Analyst, StaffRole.Board, StaffRole.PlatformAdmin })
        {
            Assert.False(RoleCapabilities.Has([role], Capability.ManageContinuityPlan), $"{role} unexpectedly manages the plan.");
            Assert.False(RoleCapabilities.Has([role], Capability.RecordProcessingDecision), $"{role} unexpectedly records the basis.");
        }
    }

    /// <summary>
    /// The continuity row names people, unlike SkillCoverageRow. That is
    /// deliberate — a plan whose owner is anonymous is not a plan — and
    /// this pins the pairing so the wider capability never starts
    /// returning it.
    /// </summary>
    [Fact]
    public void The_continuity_row_names_people_and_the_aggregate_row_still_cannot()
    {
        Assert.Contains(
            typeof(ComponentCoverageRow).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.Name == "OwnerStaffKey");

        Assert.DoesNotContain(
            typeof(SkillCoverageRow).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
    }
}
