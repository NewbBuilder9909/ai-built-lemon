namespace ProgrammePulse.Models.ServiceOps;

/// <summary>Which stream of desk records a cursor belongs to.</summary>
public enum DeskStream
{
    Tickets = 0,

    /// <summary>Deleted and spam tickets, fetched separately because most desks exclude them from the main list.</summary>
    WithdrawnTickets = 1
}

/// <summary>Same ordering and meaning as the evidence equivalent: worst wins.</summary>
public enum DeskCoverageStatus
{
    Complete = 0,
    NeverRun = 1,
    Partial = 2,
    PermissionLost = 3,
    OutOfScope = 4
}

/// <summary>
/// Incremental position and coverage honesty for one
/// (tenant, connection, stream).
///
/// <see cref="Cursor"/> is an <c>updated_since</c> watermark rather than
/// an opaque page token, because Freshdesk paginates that way. Two
/// consequences the ingestion service has to handle and this record has
/// to support: the watermark is rewound by a small overlap on each run
/// (ties at the boundary second are otherwise dropped), and a run that
/// exhausts its page budget must advance the watermark and report
/// <see cref="DeskCoverageStatus.Partial"/> rather than claim it reached
/// the end.
/// </summary>
public sealed record DeskCoverage
{
    public required Guid CoverageKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required DeskStream Stream { get; init; }

    /// <summary>The <c>updated_since</c> watermark to resume from.</summary>
    public DateTime? Cursor { get; init; }

    /// <summary>Earliest point ever fetched. Never moves forwards.</summary>
    public DateTime? ObservedFromUtc { get; init; }

    /// <summary>Only advanced by a run that reached the end of the stream cleanly.</summary>
    public DateTime? CompleteThroughUtc { get; init; }

    public required DeskCoverageStatus Status { get; init; }

    /// <summary>Reader-facing, never a raw provider body.</summary>
    public string? StatusDetail { get; init; }

    public Guid? LastRunKey { get; init; }

    public DateTime? LastAttemptedAtUtc { get; init; }

    public DateTime? LastSucceededAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public bool IsTrustworthy => Status == DeskCoverageStatus.Complete;
}

/// <summary>
/// Service demand and recurrence for one component over one period.
///
/// No person appears here, at all. Support demand belongs first to a
/// product and its owning team, per the design document, and a component
/// row is the level at which that is true. Person-level involvement lives
/// on <see cref="SupportCaseParticipant"/> behind a narrower capability.
/// </summary>
public sealed record ComponentServiceRow
{
    /// <summary>Null for the bucket of cases whose tag nobody has approved.</summary>
    public required string? ComponentKey { get; init; }

    public required string DisplayName { get; init; }

    public required int CasesOpened { get; init; }

    public required int CasesResolved { get; init; }

    /// <summary>Cases that came back after being resolved — the clearest signal a fix did not hold.</summary>
    public required int Reopened { get; init; }

    public required int HighOrUrgent { get; init; }

    /// <summary>Median, not mean: one three-week case must not move the figure for everything else.</summary>
    public required TimeSpan? MedianTimeToRestore { get; init; }

    /// <summary>Cases with a reviewer-confirmed root cause in code.</summary>
    public required int ConfirmedCodeCauses { get; init; }

    /// <summary>Cases with some link to source, of any strength — including unreviewed key matches.</summary>
    public required int LinkedToSource { get; init; }

    /// <summary>
    /// Share of resolved cases that came back. Null below a floor, because
    /// "one of one reopened = 100%" is a number that will be quoted at
    /// somebody and is worth nothing.
    /// </summary>
    public double? ReopenRate =>
        CasesResolved >= ServiceHealthThresholds.MinimumCasesForRate
            ? (double)Reopened / CasesResolved
            : null;

    /// <summary>Cases here with no link to source at all — the part of demand this view cannot explain.</summary>
    public int UnlinkedToSource => Math.Max(0, CasesOpened - LinkedToSource);

    public bool IsUnmappedComponent => ComponentKey is null;
}

/// <summary>
/// The service view, plus everything a reader needs in order not to
/// over-read it.
///
/// The caveats are part of the type rather than a paragraph in a Razor
/// file, for the same reason as the evidence portfolio: a caller cannot
/// obtain the trend without also obtaining the reasons it might be wrong.
/// </summary>
public sealed record ServiceHealthReport
{
    public required IReadOnlyList<ComponentServiceRow> Components { get; init; }

    /// <summary>The window these figures cover. Every comparison needs one.</summary>
    public required DateOnly PeriodStart { get; init; }

    public required DateOnly PeriodEnd { get; init; }

    /// <summary>The previous window of the same length, for a like-for-like comparison. Null when there is not enough history.</summary>
    public IReadOnlyList<ComponentServiceRow>? PreviousPeriod { get; init; }

    public required DeskCoverage? Coverage { get; init; }

    /// <summary>Cases in the window the desk gave no approved component for.</summary>
    public required int CasesWithUnmappedComponent { get; init; }

    /// <summary>Desk agents nobody has identified. Their resolutions reach nobody's record.</summary>
    public required int UnmappedAgents { get; init; }

    /// <summary>Withdrawn (deleted or spam) cases seen in the window, excluded from every figure above.</summary>
    public required int WithdrawnExcluded { get; init; }

    /// <summary>True until a run has completed cleanly against a live desk — so fixture data cannot pass as production.</summary>
    public required bool IsUnverifiedReplay { get; init; }

    public int TotalCasesOpened => Components.Sum(c => c.CasesOpened);

    public int TotalReopened => Components.Sum(c => c.Reopened);

    /// <summary>
    /// How much of this window's demand has any link to source at all.
    /// Prominent on purpose: a service view built on connected Git can
    /// look authoritative while explaining a small fraction of the cases.
    /// </summary>
    public double? SourceLinkCoverage =>
        TotalCasesOpened == 0 ? null : (double)Components.Sum(c => c.LinkedToSource) / TotalCasesOpened;

    public bool IsComplete => Coverage?.IsTrustworthy == true && CasesWithUnmappedComponent == 0;

    /// <summary>
    /// Nothing recorded *and* nothing reliably looked at. The difference
    /// between "this component caused no trouble" and "we cannot say",
    /// which a service report must never blur.
    /// </summary>
    public bool AbsenceIsInconclusive => TotalCasesOpened == 0 && Coverage?.IsTrustworthy != true;
}

public static class ServiceHealthThresholds
{
    /// <summary>
    /// Below this many resolved cases, a rate is not reported. Small
    /// denominators produce percentages that look precise and mean
    /// nothing, and somebody will put them in a board pack.
    /// </summary>
    public const int MinimumCasesForRate = 5;
}
