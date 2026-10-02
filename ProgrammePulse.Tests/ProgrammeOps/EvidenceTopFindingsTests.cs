using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>The order the Review leads with: what blocks the report, broken decisions, high severity, then share.</summary>
public class EvidenceTopFindingsTests
{
    private static EvidenceFinding Finding(string key, EvidenceFindingCategory category, EvidenceFindingSeverity severity, decimal numerator, decimal denominator) =>
        new(key, category, severity, key, "why", numerator, denominator, "items", []);

    private static EvidenceCheckReport Report(params EvidenceFinding[] findings) =>
        new(DateTime.UtcNow, EvidenceReadiness.UseWithCaveats, "reason", 100, 10m, findings, [], []);

    [Fact]
    public void Material_high_severity_gaps_lead_then_high_severity_then_the_largest_share()
    {
        var report = Report(
            Finding("medium-big", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, 9, 10),
            Finding("high-small", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High, 1, 100),
            Finding("blocks", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High, 50, 100),
            Finding("medium-small", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, 1, 10));

        var keys = EvidenceTopFindings.Rank(report, null).Select(t => t.Finding.Key);

        Assert.Equal(["blocks", "high-small", "medium-big", "medium-small"], keys);
    }

    [Fact]
    public void Shows_at_most_five()
    {
        var findings = Enumerable.Range(1, 8)
            .Select(i => Finding($"f{i}", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, i, 10))
            .ToArray();

        Assert.Equal(EvidenceTopFindings.Count, EvidenceTopFindings.Rank(Report(findings), null).Count);
    }

    [Fact]
    public void A_decision_that_did_not_hold_comes_before_other_findings_and_names_no_owner_once_its_records_cleared()
    {
        var owner = Guid.NewGuid();
        var report = Report(
            Finding("high", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.High, 5, 10),
            Finding("broken", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, 1, 10),
            Finding("moved-on", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, 1, 10));
        var previous = new EvidenceReview
        {
            ReviewKey = Guid.NewGuid(), TenantId = Guid.NewGuid(), RecordedAtUtc = DateTime.UtcNow.AddDays(-7),
            Readiness = EvidenceReadiness.UseWithCaveats, ReadinessReason = "r", WorkItems = 1, RecordedHours = 1m, Findings = []
        };
        var since = new EvidenceReviewComparison(previous, EvidenceReadiness.UseWithCaveats, EvidenceReadiness.UseWithCaveats,
        [
            Comparison("broken", Decided(owner), pastTarget: true, new RecordMovement(1, 0, 0)),
            Comparison("moved-on", Decided(owner), pastTarget: false, new RecordMovement(0, 1, 1))
        ]);

        var top = EvidenceTopFindings.Rank(report, since);

        Assert.Equal("broken", top[0].Finding.Key);
        Assert.True(top[0].DecisionBroken);
        Assert.Equal(owner, top[0].OwnerStaffKey);
        Assert.Null(top.Single(t => t.Finding.Key == "moved-on").OwnerStaffKey);
        Assert.Null(top.Single(t => t.Finding.Key == "high").OwnerStaffKey);
    }

    private static EvidenceReviewFinding Decided(Guid owner) => new()
    {
        FindingKey = "x", Category = EvidenceFindingCategory.DeliveryException, Severity = EvidenceFindingSeverity.Medium,
        Title = "x", WhyItMatters = "x", Numerator = 1, Denominator = 10, Unit = "items",
        OwnerStaffKey = owner, DecidedAtUtc = DateTime.UtcNow.AddDays(-7)
    };

    private static FindingComparison Comparison(string key, EvidenceReviewFinding decision, bool pastTarget, RecordMovement records) =>
        new(key, key, EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium, FindingTrend.Unchanged,
            1, 10, 1, 10, "items", decision, ResolvedButStillPresent: false, PastTargetDate: pastTarget, records);
}
