namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// What a manager decided to do about an exposure. The four the design
/// document names, plus an investigation for a recurring defect.
///
/// Every one of these is a thing a *person* does. There is deliberately
/// no value meaning "reassign", "performance-manage" or anything else
/// that would make this an employment-decision workflow — the design
/// document rules out automatic staffing and employment decisions, and
/// the enum is where that stops being a promise.
/// </summary>
public enum CoverageActionType
{
    /// <summary>Name a second person as approved cover.</summary>
    NominateBackup = 0,

    /// <summary>Pair two people on the component so knowledge moves.</summary>
    Pair = 1,

    /// <summary>Write the runbook so the knowledge is not only in a head.</summary>
    DocumentRunbook = 2,

    /// <summary>Give someone the training they would need to cover it.</summary>
    Training = 3,

    /// <summary>Look into a recurring defect or repeat support demand.</summary>
    Investigate = 4
}

/// <summary>
/// Where an action has got to. <see cref="Abandoned"/> exists so a
/// decision that was made and then dropped stays visible — a plan that
/// quietly disappears is indistinguishable from one that was never made,
/// and the second is easier to defend.
/// </summary>
public enum CoverageActionOutcome
{
    Open = 0,
    Completed = 1,
    Abandoned = 2
}

/// <summary>
/// The Platinum layer: a reviewed decision about an exposure, with an
/// owner, a rationale, the evidence it was based on, and — eventually —
/// what actually happened.
///
/// The follow-up is the point. The design document asks for "reviewed
/// actions, owners and outcomes", and a coverage report that produces
/// recommendations nobody closes is a report that gets ignored by its
/// second reading. <see cref="OutcomeNote"/> is required to complete or
/// abandon, so "done" always says what was done.
///
/// Nothing here is generated. The product can show that a component has
/// one reviewed maintainer and no backup; choosing what to do about it
/// is a management judgement with a name attached.
/// </summary>
public sealed record CoverageAction
{
    public required Guid ActionKey { get; init; }

    public required Guid TenantId { get; init; }

    public required string ComponentKey { get; init; }

    public required CoverageActionType Type { get; init; }

    /// <summary>
    /// Who is going to do it. An action cannot be *raised* without one —
    /// an action with no owner is a wish — but it becomes null when its
    /// owner is erased, so the action surfaces as needing reassignment
    /// rather than vanishing from the plan.
    ///
    /// Nullable rather than a <c>Guid.Empty</c> sentinel: an empty Guid
    /// reads as a real value in a join or a filter, and the first query
    /// that forgets the convention silently treats "unassigned" as a
    /// person.
    /// </summary>
    public Guid? OwnerStaffKey { get; init; }

    /// <summary>Why it was raised — the exposure, in the manager's words.</summary>
    public required string Rationale { get; init; }

    /// <summary>
    /// What the decision was based on, captured at the time: "one
    /// validated maintainer, last reviewed March, 14 support cases this
    /// quarter". Kept as text rather than a live query so the reasoning
    /// still reads correctly a year later, when the underlying numbers
    /// have moved.
    /// </summary>
    public string? EvidenceSnapshot { get; init; }

    public DateOnly? DueOn { get; init; }

    public required CoverageActionOutcome Outcome { get; init; }

    /// <summary>Required to complete or abandon. "Done" must say what was done.</summary>
    public string? OutcomeNote { get; init; }

    public required Guid RaisedByStaffKey { get; init; }

    public required DateTime RaisedAtUtc { get; init; }

    public Guid? ClosedByStaffKey { get; init; }

    public DateTime? ClosedAtUtc { get; init; }

    public bool IsOpen => Outcome == CoverageActionOutcome.Open;

    public bool IsOverdueOn(DateOnly asOf) => IsOpen && DueOn is { } due && due < asOf;

    /// <summary>Open, but its owner has gone. Needs a person before it means anything again.</summary>
    public bool NeedsReassignment => IsOpen && OwnerStaffKey is null;
}
