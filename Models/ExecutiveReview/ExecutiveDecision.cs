using System.ComponentModel.DataAnnotations;

namespace ProgrammePulse.Models.ExecutiveReview;

public enum DecisionStatus { Draft, Reviewed, Decided, Closed, Dismissed }
public enum EvidenceDisposition { Unreviewed, Accepted, Disputed }
public enum DecisionAction { Revise, AcceptEvidence, DisputeEvidence, Decide, Close, Dismiss, Reopen }

public sealed record DecisionDefinition(
    Guid PackKey, Guid WorkItemKey, string Title, string Materiality,
    string Options, string Recommendation, Guid OwnerStaffKey, DateOnly DueOn);

public sealed record DecisionRevision(
    Guid DecisionKey, int Version, DecisionDefinition Definition, DecisionStatus Status,
    EvidenceDisposition Evidence, string? ReviewNote, DateTime? ReviewedAtUtc,
    string? Decision, string? Rationale, Guid? ActionOwnerStaffKey, DateOnly? FollowUpOn,
    string? Outcome, string Event, string Note, int ActorMemberId, DateTime RecordedAtUtc);

public sealed class DecisionDraftInput
{
    [Required] public Guid? PackKey { get; set; }
    [Required] public Guid? WorkItemKey { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(2000)] public string Materiality { get; set; } = "";
    [Required, StringLength(4000)] public string Options { get; set; } = "";
    [Required, StringLength(2000)] public string Recommendation { get; set; } = "";
    [Required] public Guid? OwnerStaffKey { get; set; }
    [Required] public DateOnly? DueOn { get; set; }
    public DecisionDefinition ToDefinition() => new(PackKey!.Value, WorkItemKey!.Value,
        Title, Materiality, Options, Recommendation, OwnerStaffKey!.Value, DueOn!.Value);
    public static DecisionDraftInput From(DecisionDefinition definition) => new()
    {
        PackKey = definition.PackKey, WorkItemKey = definition.WorkItemKey, Title = definition.Title,
        Materiality = definition.Materiality, Options = definition.Options, Recommendation = definition.Recommendation,
        OwnerStaffKey = definition.OwnerStaffKey, DueOn = definition.DueOn
    };
}

public sealed class DecisionActionInput
{
    [Required, Range(1, int.MaxValue)] public int? ExpectedVersion { get; set; }
    [Required] public DecisionAction? Action { get; set; }
    [Required, StringLength(2000)] public string Note { get; set; } = "";
    [StringLength(2000)] public string? Decision { get; set; }
    [StringLength(4000)] public string? Rationale { get; set; }
    public Guid? ActionOwnerStaffKey { get; set; }
    public DateOnly? FollowUpOn { get; set; }
    [StringLength(4000)] public string? Outcome { get; set; }
}

public sealed record DecisionCommand(
    DecisionAction Action, string Note, DecisionDefinition? Definition = null,
    string? Decision = null, string? Rationale = null, Guid? ActionOwnerStaffKey = null,
    DateOnly? FollowUpOn = null, string? Outcome = null);

public sealed record DecisionPerson(Guid StaffKey, int MemberId, string Name, bool IsActive);
public sealed record DecisionQueue(IReadOnlyList<DecisionRevision> Decisions, DecisionStatus? Status);
public sealed record DecisionEditor(DecisionDraftInput Input, IReadOnlyList<DecisionPerson> People,
    ExecutivePack Pack, IReadOnlyList<ExecutivePack> AvailablePacks,
    Guid? DecisionKey = null, int? ExpectedVersion = null, string? ErrorKey = null);
public sealed record DecisionDetail(IReadOnlyList<DecisionRevision> History, ExecutivePack Pack,
    IReadOnlyList<DecisionPerson> People, bool CanManage, bool CanReview, bool EvidenceIsFresh,
    string? ErrorKey = null, DecisionActionInput? AttemptedAction = null)
{
    public DecisionRevision Current => History[^1];
}
