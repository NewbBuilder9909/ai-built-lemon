using System.Text.Json;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class EstimateCalibrationService(
    IEstimateBaselineRepository baselines,
    IProgrammeReadRepository programmes,
    IStaffRepository staff,
    IAuditLogRepository audit,
    TimeProvider timeProvider)
{
    public async Task<EstimateCalibrationReport> BuildReportAsync(Guid tenantId, bool includePeople)
    {
        var records = await baselines.GetForTenantAsync(tenantId);
        var items = await programmes.GetWorkItemsAsync(tenantId);
        var actualByItem = await programmes.GetLoggedHoursByWorkItemAsync(tenantId);
        var result = EstimateCalibrationCalculator.Calculate(tenantId, records, items, actualByItem);
        var people = includePeople ? await staff.GetByTenantAsync(tenantId) : [];
        var names = people.ToDictionary(p => p.StaffKey, p => p.FullName);
        var itemNames = items.ToDictionary(i => i.WorkItemKey, i => i.Title);
        return new EstimateCalibrationReport(
            result.Samples.Count, result.PendingReviewCount, result.ExcludedCount,
            result.PortfolioMedianRatio, result.PortfolioP80Ratio,
            includePeople ? result.Estimators.Select(e => new EstimatorCalibrationRow(
                e.EstimatorStaffKey, names.GetValueOrDefault(e.EstimatorStaffKey, "(unknown person)"),
                e.SampleCount, e.ObservedMedianRatio, e.AdvisoryPlanningFactor)).ToArray() : [],
            includePeople ? records.Select(r => new EstimateBaselineRow(r.EstimateBaselineKey,
                itemNames.GetValueOrDefault(r.WorkItemKey, "(unknown work item)"),
                names.GetValueOrDefault(r.EstimatorStaffKey, "(unknown person)"),
                r.OriginalEffortHours, r.CapturedAtUtc, r.ReviewedAtUtc, r.IsComparable, r.ReviewNote,
                items.FirstOrDefault(i => i.WorkItemKey == r.WorkItemKey)?.Stage == WorkItemLifecycleStage.Done))
                .OrderByDescending(r => r.CapturedAtUtc).ToArray() : [],
            includePeople ? items.Where(i => (i.Stage is WorkItemLifecycleStage.Backlog or WorkItemLifecycleStage.Ready) && i.EstimatedHours > 0
                && records.All(r => r.WorkItemKey != i.WorkItemKey))
                .Select(i => new EstimateCaptureOption(i.WorkItemKey, i.Title, i.EstimatedHours!.Value)).ToArray() : [],
            includePeople ? people.Where(p => p.IsActive).Select(p => new EstimatorOption(p.StaffKey, p.FullName)).ToArray() : [],
            includePeople);
    }

    /// <summary>Captures the baseline and audits who asked for it (moved from the controller).</summary>
    public async Task<EstimateBaseline> CaptureAsync(Guid tenantId, Guid workItemKey, Guid estimatorStaffKey, int? actorMemberId)
    {
        var item = await programmes.GetWorkItemByKeyAsync(workItemKey, tenantId);
        var person = (await staff.GetByTenantAsync(tenantId)).FirstOrDefault(p => p.StaffKey == estimatorStaffKey && p.IsActive);
        if (item is null || person is null) throw new CrossTenantReferenceException("WorkItemOrEstimator", item is null ? workItemKey : estimatorStaffKey);
        if (item.Stage is not (WorkItemLifecycleStage.Backlog or WorkItemLifecycleStage.Ready)
            || item.EstimatedHours is null or <= 0)
            throw new InvalidOperationException("Capture a positive effort estimate before work starts.");
        if ((await programmes.GetTimeEntriesForWorkItemAsync(workItemKey, tenantId)).Count > 0)
            throw new InvalidOperationException("This work item already has actual time.");

        var baseline = await baselines.CaptureAsync(new EstimateBaseline
        {
            EstimateBaselineKey = Guid.NewGuid(), TenantId = tenantId, WorkItemKey = workItemKey,
            EstimatorStaffKey = estimatorStaffKey, OriginalEffortHours = item.EstimatedHours.Value,
            CapturedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await audit.LogAsync("EstimateBaseline", baseline.EstimateBaselineKey.ToString(), "CaptureRequested", actorMemberId,
            JsonSerializer.Serialize(new { workItemKey, estimatorStaffKey, baseline.OriginalEffortHours }),
            timeProvider.GetUtcNow().UtcDateTime, tenantId);
        return baseline;
    }

    public async Task<EstimateBaseline?> ReviewAsync(Guid tenantId, Guid baselineKey, Guid reviewerStaffKey, bool comparable, string? note, int? actorMemberId)
    {
        var baseline = (await baselines.GetForTenantAsync(tenantId)).FirstOrDefault(b => b.EstimateBaselineKey == baselineKey);
        if (baseline is null) return null;
        if (baseline.ReviewedAtUtc is not null)
            throw new InvalidOperationException("This estimate has already been reviewed.");
        var item = await programmes.GetWorkItemByKeyAsync(baseline.WorkItemKey, tenantId);
        if (item?.Stage != WorkItemLifecycleStage.Done)
            throw new InvalidOperationException("Only completed work can be reviewed for calibration.");
        if (comparable && !(await programmes.GetTimeEntriesForWorkItemAsync(baseline.WorkItemKey, tenantId)).Any(t => t.DurationHours > 0))
            throw new InvalidOperationException("Comparable work needs recorded actual effort.");
        if (!comparable && string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("Explain why this estimate is not comparable.");
        var result = await baselines.ReviewAsync(tenantId, baselineKey, comparable, note?.Trim(), reviewerStaffKey, timeProvider.GetUtcNow().UtcDateTime);
        if (result is not null)
        {
            await audit.LogAsync("EstimateBaseline", result.EstimateBaselineKey.ToString(),
                result.IsComparable ? "ApprovedForCalibration" : "ExcludedFromCalibration", actorMemberId,
                JsonSerializer.Serialize(new { result.WorkItemKey, result.ReviewNote }),
                timeProvider.GetUtcNow().UtcDateTime, tenantId);
        }

        return result;
    }
}

public sealed record EstimateCalibrationReport(int SampleCount, int PendingReviewCount, int ExcludedCount,
    decimal? PortfolioMedianRatio, decimal? PortfolioP80Ratio,
    IReadOnlyList<EstimatorCalibrationRow> Estimators,
    IReadOnlyList<EstimateBaselineRow> Baselines,
    IReadOnlyList<EstimateCaptureOption> CaptureOptions,
    IReadOnlyList<EstimatorOption> EstimatorOptions,
    bool IncludesPeople = false);
public sealed record EstimatorCalibrationRow(Guid StaffKey, string Name, int SampleCount, decimal ObservedMedianRatio, decimal? AdvisoryPlanningFactor);
public sealed record EstimateBaselineRow(Guid Key, string WorkItemTitle, string EstimatorName, decimal OriginalEffortHours,
    DateTime CapturedAtUtc, DateTime? ReviewedAtUtc, bool IsComparable, string? ReviewNote, bool IsDone);
public sealed record EstimateCaptureOption(Guid WorkItemKey, string Title, decimal EstimatedHours);
public sealed record EstimatorOption(Guid StaffKey, string Name);
