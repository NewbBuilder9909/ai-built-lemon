using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Pure, source-neutral effort calibration. A reviewed estimate retains its original
/// value; ratios are advisory demand evidence, not schedule dates or staff scores.
/// </summary>
public static class EstimateCalibrationCalculator
{
    public const int MinimumBoardSamples = 10;
    public const int MinimumPersonSamples = 5;

    public static EstimateCalibrationResult Calculate(
        Guid tenantId,
        IReadOnlyList<EstimateBaseline> baselines,
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyList<TimeEntry> timeEntries) =>
        Calculate(tenantId, baselines, workItems, timeEntries.Where(t => t.TenantId == tenantId && t.WorkItemKey is not null)
            .GroupBy(t => t.WorkItemKey!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationHours)));

    /// <summary>
    /// The same calculation from hours already summed per work item, which is
    /// all it reads from time entries. The service passes the repository's
    /// tenant-scoped SQL aggregate instead of every entry the tenant has.
    /// </summary>
    public static EstimateCalibrationResult Calculate(
        Guid tenantId,
        IReadOnlyList<EstimateBaseline> baselines,
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyDictionary<Guid, decimal> actualByItem)
    {
        var items = workItems.Where(w => w.TenantId == tenantId).ToDictionary(w => w.WorkItemKey);

        var samples = new List<EstimateAccuracySample>();
        var pendingReview = 0;
        var excluded = 0;
        foreach (var baseline in baselines.Where(b => b.TenantId == tenantId))
        {
            if (!items.TryGetValue(baseline.WorkItemKey, out var item) || item.TenantId != baseline.TenantId)
            {
                excluded++;
                continue;
            }
            if (item.Stage != WorkItemLifecycleStage.Done)
            {
                continue;
            }
            if (baseline.ReviewedAtUtc is null)
            {
                pendingReview++;
                continue;
            }
            if (!baseline.IsComparable || baseline.OriginalEffortHours <= 0 ||
                !actualByItem.TryGetValue(baseline.WorkItemKey, out var actual) || actual <= 0)
            {
                excluded++;
                continue;
            }
            samples.Add(new EstimateAccuracySample(baseline.EstimatorStaffKey, baseline.WorkItemKey,
                baseline.OriginalEffortHours, actual, actual / baseline.OriginalEffortHours));
        }

        var ratios = samples.Select(s => s.ActualToEstimateRatio).Order().ToArray();
        var personRows = samples.GroupBy(s => s.EstimatorStaffKey).Select(group =>
        {
            var personalRatios = group.Select(s => s.ActualToEstimateRatio).Order().ToArray();
            return new EstimatorCalibration(group.Key, group.Count(), Median(personalRatios),
                group.Count() >= MinimumPersonSamples && ratios.Length >= MinimumBoardSamples
                    ? Math.Round((group.Count() * Median(personalRatios) + 10m * Median(ratios)) / (group.Count() + 10m), 2)
                    : null);
        }).OrderBy(r => r.EstimatorStaffKey).ToArray();

        return new EstimateCalibrationResult(samples, personRows, pendingReview, excluded,
            ratios.Length >= MinimumBoardSamples ? Median(ratios) : null,
            ratios.Length >= MinimumBoardSamples ? Percentile80(ratios) : null);
    }

    private static decimal Median(decimal[] ordered) => ordered.Length % 2 == 1
        ? ordered[ordered.Length / 2]
        : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2m;

    private static decimal Percentile80(decimal[] ordered) =>
        ordered[(int)Math.Ceiling(ordered.Length * 0.8m) - 1];
}

public sealed record EstimateAccuracySample(Guid EstimatorStaffKey, Guid WorkItemKey,
    decimal OriginalEffortHours, decimal ActualEffortHours, decimal ActualToEstimateRatio);

public sealed record EstimatorCalibration(Guid EstimatorStaffKey, int SampleCount,
    decimal ObservedMedianRatio, decimal? AdvisoryPlanningFactor);

public sealed record EstimateCalibrationResult(
    IReadOnlyList<EstimateAccuracySample> Samples,
    IReadOnlyList<EstimatorCalibration> Estimators,
    int PendingReviewCount,
    int ExcludedCount,
    decimal? PortfolioMedianRatio,
    decimal? PortfolioP80Ratio);
