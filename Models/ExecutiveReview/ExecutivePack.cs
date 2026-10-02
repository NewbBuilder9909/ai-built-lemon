using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.ExecutiveReview;

// Deliberately excludes rates, costs, staff identities and free-text task titles.
public sealed record WorkItemEvidence(
    Guid WorkItemKey, string ExternalId, WorkItemLifecycleStage Stage,
    DateTime? DueAtUtc, bool IsMilestone, DateTime ObservedUpdatedAtUtc);

public sealed record ExecutiveMetrics(int Total, int Open, int Blocked, int Unmapped, int OpenWithoutDueDate, int Overdue);

public sealed record ExecutivePack(
    Guid PackKey, Guid? PreviousPackKey, DateTime CapturedAtUtc,
    string DefinitionVersion, MarketSettingsVersion Market,
    string Source, Guid ConnectionKey, string AccountAtCapture, Guid PublicationRunKey,
    DateTime PublishedAtUtc, int MaximumPublicationAgeHours,
    IReadOnlyList<WorkItemEvidence> Items, ExecutiveMetrics Metrics)
{
    // Stored totals, not recalculated using a future release's metric definitions.
    public int Total => Metrics.Total;
    public int Open => Metrics.Open;
    public int Blocked => Metrics.Blocked;
    public int Unmapped => Metrics.Unmapped;
    public int OpenWithoutDueDate => Metrics.OpenWithoutDueDate;
    public int Overdue => Metrics.Overdue;
}

public sealed record ReviewSource(string Name, string DisplayName);
public sealed record ExecutiveReviewViewModel(
    IReadOnlyList<ReviewSource> Sources, IReadOnlyList<ExecutivePack> Packs,
    bool CanCapture, string? ErrorKey = null);
