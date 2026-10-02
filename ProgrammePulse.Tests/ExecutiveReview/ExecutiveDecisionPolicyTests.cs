using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public sealed class ExecutiveDecisionPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Review_decision_outcome_and_reopen_keep_previous_versions_unchanged()
    {
        var pack = Pack();
        var draft = Draft(pack);
        var reviewed = Apply(draft, new(DecisionAction.AcceptEvidence, "Checked publication"), pack);
        var decided = Apply(reviewed, Decide(), pack);
        var closed = Apply(decided, new(DecisionAction.Close, "Follow-up complete", Outcome: "Milestone recovered"), pack);
        var reopened = Apply(closed, new(DecisionAction.Reopen, "Further work"), pack);
        Assert.Equal(DecisionStatus.Draft, draft.Status);
        Assert.Equal(EvidenceDisposition.Accepted, reviewed.Evidence);
        Assert.Equal("Proceed", decided.Decision);
        Assert.Equal("Milestone recovered", closed.Outcome);
        Assert.Equal(5, reopened.Version);
        Assert.Equal(DecisionStatus.Draft, reopened.Status);
        Assert.Equal(EvidenceDisposition.Unreviewed, reopened.Evidence);
        Assert.Null(reopened.Decision);
        Assert.Null(reopened.ReviewedAtUtc);
        Assert.Null(reopened.Outcome);
        Assert.Equal(42, reopened.ActorMemberId);
        Assert.Equal(Now, reopened.RecordedAtUtc);
    }

    [Theory]
    [InlineData(DecisionStatus.Draft, DecisionAction.Decide)]
    [InlineData(DecisionStatus.Draft, DecisionAction.Close)]
    [InlineData(DecisionStatus.Reviewed, DecisionAction.Close)]
    [InlineData(DecisionStatus.Decided, DecisionAction.Revise)]
    [InlineData(DecisionStatus.Decided, DecisionAction.AcceptEvidence)]
    [InlineData(DecisionStatus.Closed, DecisionAction.Decide)]
    [InlineData(DecisionStatus.Dismissed, DecisionAction.AcceptEvidence)]
    [InlineData(DecisionStatus.Draft, (DecisionAction)999)]
    public void Lifecycle_rejects_skips_and_invalid_actions(DecisionStatus status, DecisionAction action)
    {
        var pack = Pack();
        Assert.False(ExecutiveDecisionPolicy.CanApply(status, action));
        Assert.Throws<ReviewValidationException>(() => Apply(Draft(pack) with { Status = status }, new(action, "note"), pack));
    }

    [Fact]
    public void Dispute_and_revision_invalidate_acceptance_and_require_a_new_review()
    {
        var pack = Pack();
        var reviewed = Apply(Draft(pack), new(DecisionAction.AcceptEvidence, "checked"), pack);
        var disputed = Apply(reviewed, new(DecisionAction.DisputeEvidence, "Incorrect mapping"), pack);
        Assert.Equal(DecisionStatus.Draft, disputed.Status);
        Assert.Equal(EvidenceDisposition.Disputed, disputed.Evidence);
        Assert.Throws<ReviewValidationException>(() => Apply(disputed, Decide(), pack));
        var revised = Apply(reviewed, new(DecisionAction.Revise, "Changed recommendation",
            reviewed.Definition with { Recommendation = " Defer " }), pack);
        Assert.Equal("Defer", revised.Definition.Recommendation);
        Assert.Equal(EvidenceDisposition.Unreviewed, revised.Evidence);
        Assert.Null(revised.ReviewNote);
        Assert.Null(revised.ReviewedAtUtc);
        Assert.Throws<ReviewValidationException>(() => Apply(revised, Decide(), pack));
    }

    [Theory]
    [InlineData(DecisionAction.AcceptEvidence)]
    [InlineData(DecisionAction.Decide)]
    public void Both_acceptance_and_decision_recheck_freshness_identity_and_mapping(DecisionAction action)
    {
        var pack = Pack();
        var current = action == DecisionAction.Decide ? Apply(Draft(pack), new(DecisionAction.AcceptEvidence, "checked"), pack) : Draft(pack);
        var command = action == DecisionAction.Decide ? Decide() : new DecisionCommand(action, "checked");
        ExecutivePack[] invalid = [pack with { PublishedAtUtc = Now.AddHours(-49) },
            pack with { CapturedAtUtc = Now.AddMinutes(1) }, pack with { PublishedAtUtc = Now.AddMinutes(1) },
            pack with { PackKey = Guid.NewGuid() }, pack with { Items = [] },
            pack with { Items = [pack.Items[0] with { Stage = WorkItemLifecycleStage.Unmapped }] }];
        foreach (var evidence in invalid)
            Assert.Throws<ReviewValidationException>(() => Apply(current, command, evidence));
        Assert.Equal(action == DecisionAction.Decide ? DecisionStatus.Decided : DecisionStatus.Reviewed, Apply(current, command, pack).Status);
    }

    [Fact]
    public void Freshness_uses_the_stricter_saved_or_current_limit_and_preserves_unknown_due_dates()
    {
        var pack = Pack() with { PublishedAtUtc = Now.AddHours(-2), MaximumPublicationAgeHours = 1 };
        Assert.False(ExecutiveDecisionPolicy.IsFresh(pack, Now, 48));
        Assert.False(ExecutiveDecisionPolicy.IsFresh(pack with { MaximumPublicationAgeHours = 48 }, Now, 1));
        Assert.False(ExecutiveDecisionPolicy.IsFresh(pack, Now, 0));
        Assert.False(ExecutiveDecisionPolicy.IsFresh(pack with { MaximumPublicationAgeHours = 169 }, Now, 48));
        Assert.True(ExecutiveDecisionPolicy.IsFresh(pack with { MaximumPublicationAgeHours = 48 }, Now, 2));
        var unknownDue = Pack();
        Assert.Null(unknownDue.Items[0].DueAtUtc);
        Assert.Equal(DecisionStatus.Reviewed, Apply(Draft(unknownDue), new(DecisionAction.AcceptEvidence, "Missing due date noted"), unknownDue).Status);
    }

    [Fact]
    public void Required_fields_notes_and_lengths_are_enforced_outside_MVC()
    {
        var pack = Pack();
        var draft = Draft(pack);
        DecisionDefinition[] invalid = [draft.Definition with { PackKey = Guid.Empty },
            draft.Definition with { WorkItemKey = Guid.Empty }, draft.Definition with { OwnerStaffKey = Guid.Empty },
            draft.Definition with { DueOn = default }, draft.Definition with { Title = " " },
            draft.Definition with { Title = new string('x', 201) }, draft.Definition with { Materiality = "" },
            draft.Definition with { Options = new string('x', 4001) }, draft.Definition with { Recommendation = "" }];
        foreach (var definition in invalid) Assert.Throws<ReviewValidationException>(() => ExecutiveDecisionPolicy.Validate(definition));
        foreach (var note in new[] { "", "  ", new string('x', 2001) })
            Assert.Throws<ReviewValidationException>(() => Apply(draft, new(DecisionAction.AcceptEvidence, note), pack));
        var reviewed = Apply(draft, new(DecisionAction.AcceptEvidence, "checked"), pack);
        DecisionCommand[] incomplete = [Decide() with { Decision = null }, Decide() with { Rationale = " " },
            Decide() with { ActionOwnerStaffKey = null }, Decide() with { FollowUpOn = null }];
        foreach (var command in incomplete) Assert.Throws<ReviewValidationException>(() => Apply(reviewed, command, pack));
        var decided = Apply(reviewed, Decide(), pack);
        Assert.Throws<ReviewValidationException>(() => Apply(decided, new(DecisionAction.Close, "closed"), pack));
        Assert.Throws<ReviewValidationException>(() => Apply(reviewed with { Evidence = EvidenceDisposition.Disputed }, Decide(), pack));
    }

    [Fact]
    public void Decision_management_and_review_are_explicit_tenant_manager_capabilities()
    {
        foreach (var capability in new[] { Capability.ManageExecutiveDecisions, Capability.ReviewExecutiveEvidence })
            Assert.Equal(new[] { StaffRole.Admin, StaffRole.TeamLead }.Order(), RoleCapabilities.RolesGranting(capability).Order());
    }

    private static DecisionCommand Decide() => new(DecisionAction.Decide, "Meeting record", Decision: "Proceed",
        Rationale: "Lowest exposure", ActionOwnerStaffKey: Guid.NewGuid(), FollowUpOn: new(2026, 9, 30));

    private static DecisionRevision Apply(DecisionRevision current, DecisionCommand command, ExecutivePack pack) =>
        ExecutiveDecisionPolicy.Apply(current, command, pack, 42, Now, 48);

    private static DecisionRevision Draft(ExecutivePack pack) => new(Guid.NewGuid(), 1,
        new(pack.PackKey, pack.Items[0].WorkItemKey, "Recover milestone", "Customer impact", "Defer or resource", "Resource", Guid.NewGuid(), new(2026, 9, 25)),
        DecisionStatus.Draft, EvidenceDisposition.Unreviewed, null, null, null, null, null, null, null, "Created", "", 1, Now.AddMinutes(-1));

    private static ExecutivePack Pack()
    {
        WorkItemEvidence[] items = [new(Guid.NewGuid(), "source-item", WorkItemLifecycleStage.Blocked, null, true, Now.AddHours(-1))];
        return new(Guid.NewGuid(), null, Now.AddMinutes(-1), "operational-evidence-v1", new(0, new(), null, null),
            "ClickUp", Guid.NewGuid(), "account", Guid.NewGuid(), Now.AddHours(-1), 48, items, ExecutivePackPolicy.Summarize(items, Now));
    }
}
