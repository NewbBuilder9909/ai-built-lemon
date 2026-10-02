namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>One of the findings the Review page leads with, and what last week's review decided about it.</summary>
public sealed record TopFinding(EvidenceFinding Finding, FindingComparison? SinceLastReview)
{
    /// <summary>The owner last review named, while the records it covered are still here; otherwise nobody.</summary>
    public Guid? OwnerStaffKey =>
        SinceLastReview is { PreviousDecision: { DecidedAtUtc: not null } decision } since && !since.DecisionNotCarried
            ? decision.OwnerStaffKey
            : null;

    /// <summary>Last review's decision didn't hold: marked resolved, or past its target date, and still found.</summary>
    public bool DecisionBroken => SinceLastReview is { } since && (since.ResolvedButStillPresent || since.PastTargetDate);
}

/// <summary>
/// The few findings to look at first, in a stated order: gaps large enough
/// to make the report not decision-ready, then decisions that didn't hold,
/// then other high-severity findings, then the largest share of its own
/// total. Pure, so the order has a test.
/// </summary>
public static class EvidenceTopFindings
{
    public const int Count = 5;

    public static IReadOnlyList<TopFinding> Rank(EvidenceCheckReport report, EvidenceReviewComparison? sinceLastReview)
    {
        var comparisons = sinceLastReview?.Findings.ToDictionary(f => f.FindingKey, StringComparer.Ordinal)
            ?? new Dictionary<string, FindingComparison>(StringComparer.Ordinal);

        return report.Findings
            .Select(f => new TopFinding(f, comparisons.GetValueOrDefault(f.Key)))
            .OrderByDescending(t => BlocksTheReport(t.Finding))
            .ThenByDescending(t => t.DecisionBroken)
            .ThenByDescending(t => t.Finding.Severity == EvidenceFindingSeverity.High)
            .ThenByDescending(t => Share(t.Finding))
            .ThenBy(t => t.Finding.Key, StringComparer.Ordinal)
            .Take(Count)
            .ToList();
    }

    public static bool BlocksTheReport(EvidenceFinding finding) =>
        finding.Category == EvidenceFindingCategory.EvidenceGap
        && finding.Severity == EvidenceFindingSeverity.High
        && finding.IsMaterial;

    private static decimal Share(EvidenceFinding finding) =>
        finding.Denominator > 0 ? finding.Numerator / finding.Denominator : 0;
}
