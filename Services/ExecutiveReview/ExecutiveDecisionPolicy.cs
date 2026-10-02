using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ExecutiveReview;

public static class ExecutiveDecisionPolicy
{
    public static DecisionDefinition Validate(DecisionDefinition definition)
    {
        if (definition.PackKey == Guid.Empty || definition.WorkItemKey == Guid.Empty
            || definition.OwnerStaffKey == Guid.Empty || definition.DueOn == default)
            throw new ReviewValidationException("Decision.InvalidFields");
        return definition with
        {
            Title = Required(definition.Title, 200), Materiality = Required(definition.Materiality, 2000),
            Options = Required(definition.Options, 4000), Recommendation = Required(definition.Recommendation, 2000)
        };
    }

    public static bool IsFresh(ExecutivePack pack, DateTime nowUtc, int maximumAgeHours) =>
        maximumAgeHours is >= 1 and <= 168 && pack.MaximumPublicationAgeHours is >= 1 and <= 168
        && pack.PublishedAtUtc <= pack.CapturedAtUtc && pack.CapturedAtUtc <= nowUtc
        && nowUtc - pack.PublishedAtUtc <= TimeSpan.FromHours(Math.Min(maximumAgeHours, pack.MaximumPublicationAgeHours));

    public static bool CanApply(DecisionStatus status, DecisionAction action) => action switch
    {
        DecisionAction.Revise or DecisionAction.DisputeEvidence or DecisionAction.Dismiss => status is DecisionStatus.Draft or DecisionStatus.Reviewed,
        DecisionAction.AcceptEvidence => status == DecisionStatus.Draft,
        DecisionAction.Decide => status == DecisionStatus.Reviewed,
        DecisionAction.Close => status == DecisionStatus.Decided,
        DecisionAction.Reopen => status is DecisionStatus.Closed or DecisionStatus.Dismissed,
        _ => false
    };

    public static DecisionRevision Apply(DecisionRevision current, DecisionCommand command,
        ExecutivePack pack, int actorMemberId, DateTime nowUtc, int maximumAgeHours)
    {
        if (!CanApply(current.Status, command.Action)) throw new ReviewValidationException("Decision.InvalidTransition");
        var next = current with
        {
            Version = checked(current.Version + 1), Event = command.Action.ToString(),
            Note = Required(command.Note, 2000), ActorMemberId = actorMemberId, RecordedAtUtc = nowUtc
        };
        if (command.Action is DecisionAction.AcceptEvidence or DecisionAction.Decide)
        {
            var evidence = pack.Items.SingleOrDefault(i => i.WorkItemKey == current.Definition.WorkItemKey);
            if (pack.PackKey != current.Definition.PackKey || evidence is null)
                throw new ReviewValidationException("Decision.InvalidEvidence");
            if (!IsFresh(pack, nowUtc, maximumAgeHours)) throw new ReviewValidationException("Decision.StaleEvidence");
            if (evidence.Stage == WorkItemLifecycleStage.Unmapped)
                throw new ReviewValidationException("Decision.UnmappedEvidence");
        }
        return command.Action switch
        {
            DecisionAction.Revise => Reset(next) with { Definition = Validate(command.Definition ?? throw new ReviewValidationException("Decision.InvalidFields")) },
            DecisionAction.AcceptEvidence => next with { Status = DecisionStatus.Reviewed, Evidence = EvidenceDisposition.Accepted, ReviewNote = next.Note, ReviewedAtUtc = nowUtc },
            DecisionAction.DisputeEvidence => next with { Status = DecisionStatus.Draft, Evidence = EvidenceDisposition.Disputed, ReviewNote = next.Note, ReviewedAtUtc = nowUtc },
            DecisionAction.Decide when current.Evidence == EvidenceDisposition.Accepted && current.ReviewedAtUtc is not null => next with
            {
                Status = DecisionStatus.Decided, Decision = Required(command.Decision, 2000), Rationale = Required(command.Rationale, 4000),
                ActionOwnerStaffKey = command.ActionOwnerStaffKey is Guid key && key != Guid.Empty ? key : throw new ReviewValidationException("Decision.InvalidOwner"),
                FollowUpOn = command.FollowUpOn is DateOnly date && date != default ? date : throw new ReviewValidationException("Decision.InvalidFields")
            },
            DecisionAction.Close => next with { Status = DecisionStatus.Closed, Outcome = Required(command.Outcome, 4000) },
            DecisionAction.Dismiss => next with { Status = DecisionStatus.Dismissed },
            DecisionAction.Reopen => Reset(next),
            _ => throw new ReviewValidationException("Decision.InvalidTransition")
        };
    }

    private static DecisionRevision Reset(DecisionRevision revision) => revision with
    {
        Status = DecisionStatus.Draft, Evidence = EvidenceDisposition.Unreviewed, ReviewNote = null, ReviewedAtUtc = null,
        Decision = null, Rationale = null, ActionOwnerStaffKey = null, FollowUpOn = null, Outcome = null
    };

    private static string Required(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength
            ? value.Trim() : throw new ReviewValidationException("Decision.InvalidFields");
}
