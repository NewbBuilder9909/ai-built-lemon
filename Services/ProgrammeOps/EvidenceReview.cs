namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// What the review decided about one finding. Stated as the action, not a
/// status, because the review's job is to leave every finding with an owner
/// and a next step (docs/commercial/evidence-pack-template.md, "disposition").
/// </summary>
public enum FindingDisposition
{
    /// <summary>Recorded but not yet decided. The default for a new finding.</summary>
    Open,

    /// <summary>The data is wrong or missing at source; an owner fixes it there by a target date.</summary>
    FixAtSource,

    /// <summary>The gap stays, and is stated alongside the figures it affects.</summary>
    AcceptAndState,

    /// <summary>The reviewer believes the finding doesn't hold. A reason is required.</summary>
    Disputed,

    /// <summary>The reviewer says it's fixed. The next recorded check tests that claim.</summary>
    Resolved
}

/// <summary>
/// One recorded Evidence Check: a frozen copy of the check as it stood when
/// the review took place, with the decisions made about it. Live Silver data
/// keeps changing after the review; the pack for that review must not.
/// </summary>
public sealed record EvidenceReview
{
    public required Guid ReviewKey { get; init; }

    public required Guid TenantId { get; init; }

    public required DateTime RecordedAtUtc { get; init; }

    public Guid? RecordedByStaffKey { get; init; }

    public required EvidenceReadiness Readiness { get; init; }

    public required string ReadinessReason { get; init; }

    public required int WorkItems { get; init; }

    public required decimal RecordedHours { get; init; }

    public IReadOnlyList<ProjectEvidenceSummary> Projects { get; init; } = [];

    public IReadOnlyList<string> SourceNotes { get; init; } = [];

    public IReadOnlyList<EvidenceReviewFinding> Findings { get; init; } = [];

    /// <summary>
    /// What the review covered. Null for reviews recorded before scopes
    /// existed: those read the whole organisation over all time, and are
    /// compared as whole-organisation reviews.
    /// </summary>
    public EvidenceScope? Scope { get; init; }

    public string ScopeKey => Scope?.Key ?? EvidenceScope.WholeOrganisationKey;
}

/// <summary>
/// A finding as recorded, plus its decision. <see cref="CarriedForward"/>
/// means the decision was copied from the previous review and nobody has
/// revisited it since, which the review page says out loud: an owner named
/// three weeks ago is not the same as an owner confirmed this week.
/// </summary>
public sealed record EvidenceReviewFinding
{
    public required string FindingKey { get; init; }

    public required EvidenceFindingCategory Category { get; init; }

    public required EvidenceFindingSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string WhyItMatters { get; init; }

    public required decimal Numerator { get; init; }

    public required decimal Denominator { get; init; }

    public required string Unit { get; init; }

    public IReadOnlyList<EvidenceRecord> Records { get; init; } = [];

    public FindingDisposition Disposition { get; init; } = FindingDisposition.Open;

    public Guid? OwnerStaffKey { get; init; }

    public DateOnly? TargetDate { get; init; }

    public string? Note { get; init; }

    public DateTime? DecidedAtUtc { get; init; }

    public Guid? DecidedByStaffKey { get; init; }

    public bool CarriedForward { get; init; }

    public bool IsMaterial => Denominator > 0 && Numerator / Denominator >= EvidenceCheckCalculator.MaterialShare;
}

/// <summary>How a finding moved between two recorded checks.</summary>
public enum FindingTrend
{
    New,
    Worse,

    /// <summary>
    /// Same count, different records: some cleared and as many new ones
    /// appeared. Never reported as "no change", because ten fixed items
    /// and ten newly late ones are not the same problem.
    /// </summary>
    Shifted,

    Unchanged,
    Improved,
    Cleared
}

/// <summary>
/// Which source records behind a finding stayed, appeared or cleared between
/// two reviews. Records are matched on <see cref="EvidenceRecordIdentity"/>.
/// </summary>
public sealed record RecordMovement(int StillPresent, int Appeared, int Cleared);

/// <summary>
/// How one source record is recognised in the next review: its source and
/// external id, which every sync source and the file import set. A record
/// without one falls back to project and title, which can merge two
/// same-named records but never splits one.
/// </summary>
public static class EvidenceRecordIdentity
{
    public static string Of(EvidenceRecord record) =>
        string.IsNullOrEmpty(record.ExternalId)
            ? $"title\u001f{record.Project}\u001f{record.Title}"
            : $"id\u001f{record.Source}\u001f{record.ExternalId}";
}

/// <summary>
/// One finding across two checks. Movement is judged on the numerator — the
/// number of things someone has to fix — not the share, so a finding can't
/// look better just because more clean data arrived around it. Both figures
/// are shown so a reader can see the share too.
///
/// <see cref="Records"/> says which records persisted, appeared and cleared;
/// it's null when either side has no records to match (a finding such as
/// "people awaiting a match" is a count with no rows), and then the movement
/// is a count comparison only.
/// </summary>
public sealed record FindingComparison(
    string FindingKey,
    string Title,
    EvidenceFindingCategory Category,
    EvidenceFindingSeverity Severity,
    FindingTrend Trend,
    decimal? PreviousNumerator,
    decimal? PreviousDenominator,
    decimal? CurrentNumerator,
    decimal? CurrentDenominator,
    string Unit,
    EvidenceReviewFinding? PreviousDecision,
    bool ResolvedButStillPresent,
    bool PastTargetDate,
    RecordMovement? Records = null)
{
    /// <summary>
    /// Last review decided this finding, but none of the records it covered
    /// remain: the decision was not carried to the records now found.
    /// </summary>
    public bool DecisionNotCarried =>
        PreviousDecision?.DecidedAtUtc is not null && CurrentNumerator is not null && Records is { StillPresent: 0 };
}

public sealed record EvidenceReviewComparison(
    EvidenceReview Previous,
    EvidenceReadiness PreviousReadiness,
    EvidenceReadiness CurrentReadiness,
    IReadOnlyList<FindingComparison> Findings)
{
    public int Improved => Findings.Count(f => f.Trend is FindingTrend.Improved or FindingTrend.Cleared);

    public int Worse => Findings.Count(f => f.Trend is FindingTrend.Worse or FindingTrend.New);

    /// <summary>Same count as last time, but not the same records.</summary>
    public int Shifted => Findings.Count(f => f.Trend == FindingTrend.Shifted);

    /// <summary>Decisions that didn't stick: marked resolved, or past their target date, and still found.</summary>
    public int BrokenCommitments => Findings.Count(f => f.ResolvedButStillPresent || f.PastTargetDate);
}

/// <summary>
/// The rules that turn a one-off check into a weekly review loop. Pure, so
/// each rule below has a unit test that states it.
///
/// - <b>Record</b> freezes a check. Each finding inherits the previous
///   review's decision for the same finding key, marked as carried forward,
///   provided at least one of the records that decision covered is still
///   found. If every one has cleared, the finding now describes different
///   records, and an owner named for last week's items is not an owner of
///   this week's, so it starts undecided.
///   A finding previously marked <see cref="FindingDisposition.Resolved"/>
///   whose records are still present goes back to <see cref="FindingDisposition.Open"/>:
///   the data has contradicted the claim, so the claim can't stand.
/// - <b>Compare</b> sets each finding against the previous review, record by
///   record where both sides have records: an unchanged count over different
///   records is <see cref="FindingTrend.Shifted"/>, never "no change".
/// - <b>Decide</b> validates a decision: fixing at source needs an owner and
///   a target date; accepting or disputing needs a written reason.
/// </summary>
public static class EvidenceReviewCalculator
{
    public const int MaxNoteLength = 1000;

    public static EvidenceReview Record(EvidenceCheckReport report, EvidenceReview? previous, Guid tenantId, Guid? recordedBy, Guid reviewKey)
    {
        // Decisions only carry between reviews of the same programme and customer.
        if (previous is not null && previous.ScopeKey != (report.Scope?.Key ?? EvidenceScope.WholeOrganisationKey))
            previous = null;
        var previousByKey = previous?.Findings.ToDictionary(f => f.FindingKey) ?? [];
        var findings = report.Findings.Select(finding =>
        {
            var recorded = new EvidenceReviewFinding
            {
                FindingKey = finding.Key,
                Category = finding.Category,
                Severity = finding.Severity,
                Title = finding.Title,
                WhyItMatters = finding.WhyItMatters,
                Numerator = finding.Numerator,
                Denominator = finding.Denominator,
                Unit = finding.Unit,
                Records = finding.Records
            };
            if (!previousByKey.TryGetValue(finding.Key, out var before) || before.DecidedAtUtc is null)
                return recorded;
            if (Movement(before, recorded) is { StillPresent: 0 })
                return recorded;

            return recorded with
            {
                Disposition = before.Disposition == FindingDisposition.Resolved ? FindingDisposition.Open : before.Disposition,
                OwnerStaffKey = before.OwnerStaffKey,
                TargetDate = before.TargetDate,
                Note = before.Note,
                DecidedAtUtc = before.DecidedAtUtc,
                DecidedByStaffKey = before.DecidedByStaffKey,
                CarriedForward = true
            };
        }).ToList();

        return new EvidenceReview
        {
            ReviewKey = reviewKey,
            TenantId = tenantId,
            RecordedAtUtc = report.AsOfUtc,
            RecordedByStaffKey = recordedBy,
            Readiness = report.Readiness,
            ReadinessReason = report.ReadinessReason,
            WorkItems = report.WorkItems,
            RecordedHours = report.RecordedHours,
            Projects = report.Projects,
            SourceNotes = report.SourceNotes,
            Findings = findings,
            Scope = report.Scope
        };
    }

    public static EvidenceReviewComparison Compare(EvidenceReview current, EvidenceReview previous, DateOnly today)
    {
        var currentByKey = current.Findings.ToDictionary(f => f.FindingKey);
        var previousByKey = previous.Findings.ToDictionary(f => f.FindingKey);

        var rows = new List<FindingComparison>();
        foreach (var now in current.Findings)
        {
            previousByKey.TryGetValue(now.FindingKey, out var before);
            var movement = before is null
                ? now.Records.Count == 0 ? null : new RecordMovement(0, now.Records.Count, 0)
                : Movement(before, now);
            var trend = before is null ? FindingTrend.New
                : now.Numerator < before.Numerator ? FindingTrend.Improved
                : now.Numerator > before.Numerator ? FindingTrend.Worse
                : movement is { Appeared: > 0 } or { Cleared: > 0 } ? FindingTrend.Shifted
                : FindingTrend.Unchanged;
            // A decision "didn't hold" only if the records it was about are still
            // here; when all of them cleared, the claim held and new records appeared.
            var stillHeld = movement is null or { StillPresent: > 0 };
            rows.Add(new FindingComparison(
                now.FindingKey, now.Title, now.Category, now.Severity, trend,
                before?.Numerator, before?.Denominator, now.Numerator, now.Denominator, now.Unit,
                before,
                ResolvedButStillPresent: before?.Disposition == FindingDisposition.Resolved && stillHeld,
                PastTargetDate: before?.TargetDate is { } target && target < today && before.Disposition != FindingDisposition.Resolved && stillHeld,
                Records: movement));
        }

        foreach (var before in previous.Findings.Where(f => !currentByKey.ContainsKey(f.FindingKey)))
        {
            rows.Add(new FindingComparison(
                before.FindingKey, before.Title, before.Category, before.Severity, FindingTrend.Cleared,
                before.Numerator, before.Denominator, null, null, before.Unit,
                before, ResolvedButStillPresent: false, PastTargetDate: false,
                Records: before.Records.Count == 0 ? null : new RecordMovement(0, 0, before.Records.Count)));
        }

        var ordered = rows
            .OrderBy(r => r.Category)
            .ThenBy(r => TrendOrder(r.Trend))
            .ThenBy(r => r.Severity)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ToList();

        return new EvidenceReviewComparison(previous, previous.Readiness, current.Readiness, ordered);
    }

    /// <summary>Null when either side has no records to match, so only the counts can be compared.</summary>
    public static RecordMovement? Movement(EvidenceReviewFinding before, EvidenceReviewFinding now)
    {
        if (before.Records.Count == 0 || now.Records.Count == 0)
            return null;

        var previous = before.Records.Select(EvidenceRecordIdentity.Of).ToHashSet(StringComparer.Ordinal);
        var current = now.Records.Select(EvidenceRecordIdentity.Of).ToHashSet(StringComparer.Ordinal);
        var stillPresent = current.Count(previous.Contains);
        return new RecordMovement(stillPresent, current.Count - stillPresent, previous.Count - stillPresent);
    }

    /// <summary>Null when the decision is acceptable; otherwise the reason it isn't, in words a reviewer can act on.</summary>
    public static string? Validate(FindingDisposition disposition, Guid? ownerStaffKey, DateOnly? targetDate, string? note)
    {
        if (!Enum.IsDefined(disposition))
            return "Choose a decision.";
        if (note is { Length: > MaxNoteLength })
            return $"The note is longer than {MaxNoteLength} characters.";
        return disposition switch
        {
            FindingDisposition.FixAtSource when ownerStaffKey is null => "Fixing at source needs a named owner.",
            FindingDisposition.FixAtSource when targetDate is null => "Fixing at source needs a target date.",
            FindingDisposition.AcceptAndState when string.IsNullOrWhiteSpace(note) =>
                "Accepting a gap needs the caveat that will be stated with the figures.",
            FindingDisposition.Disputed when string.IsNullOrWhiteSpace(note) => "Disputing a finding needs a reason.",
            _ => null
        };
    }

    public static string Label(FindingDisposition disposition) => disposition switch
    {
        FindingDisposition.FixAtSource => "Fix at source",
        FindingDisposition.AcceptAndState => "Accept and state with the figures",
        FindingDisposition.Disputed => "Disputed",
        FindingDisposition.Resolved => "Resolved",
        _ => "Not yet decided"
    };

    public static string Label(FindingTrend trend) => trend switch
    {
        FindingTrend.New => "New",
        FindingTrend.Worse => "Worse",
        FindingTrend.Improved => "Improved",
        FindingTrend.Cleared => "Cleared",
        FindingTrend.Shifted => "Same count, different records",
        _ => "No change"
    };

    public static string Label(RecordMovement? movement) => movement is null
        ? "Count only — no records to match"
        : $"{movement.StillPresent:N0} still found, {movement.Appeared:N0} new, {movement.Cleared:N0} cleared";

    private static int TrendOrder(FindingTrend trend) => trend switch
    {
        FindingTrend.New => 0,
        FindingTrend.Worse => 1,
        FindingTrend.Shifted => 2,
        FindingTrend.Unchanged => 3,
        FindingTrend.Improved => 4,
        _ => 5
    };
}
