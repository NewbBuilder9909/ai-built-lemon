using System.Text.Json;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>Who is acting, as far as the review and the audit log need to know.</summary>
public sealed record EvidenceReviewActor(Guid? StaffKey, int? MemberId);

public sealed record StaffOption(Guid StaffKey, string Name);

/// <summary>The live check, set against the last recorded review when there is one.</summary>
public sealed record EvidenceCheckPage(
    EvidenceCheckReport Report,
    EvidenceReview? LastReview,
    EvidenceReviewComparison? SinceLastReview,
    IReadOnlyDictionary<Guid, string> StaffNames)
{
    /// <summary>Whether the viewer may record this check (CaptureTrend). Set by the controller.</summary>
    public bool CanRecord { get; init; }

    /// <summary>A record was attempted with no work items to freeze.</summary>
    public bool NothingToRecord { get; init; }

    /// <summary>The programme and customer lists for the scope form. Set by the controller.</summary>
    public Models.ViewModels.ProgrammeOverview.PortfolioScopeViewModel? ScopePicker { get; init; }

    /// <summary>Why the requested scope was refused; the page then shows the default scope.</summary>
    public string? ScopeError { get; init; }
}

/// <summary>
/// One recorded review. Only the latest can take decisions: once a later
/// check is recorded, this one is what the review knew at the time.
/// </summary>
public sealed record EvidenceReviewPage(
    EvidenceReview Review,
    EvidenceReviewComparison? SincePrevious,
    bool IsLatest,
    IReadOnlyDictionary<Guid, string> StaffNames,
    IReadOnlyList<StaffOption> OwnerOptions,
    string? TenantName)
{
    /// <summary>The latest review, and the viewer holds ManageExecutiveDecisions. Set by the controller.</summary>
    public bool CanDecide { get; init; }

    /// <summary>The finding just saved, so the page can confirm it.</summary>
    public string? SavedFindingKey { get; init; }

    /// <summary>Why the last decision was refused, shown against <see cref="DecisionFindingKey"/>.</summary>
    public string? DecisionError { get; init; }

    public string? DecisionFindingKey { get; init; }
}

public enum EvidenceDecisionStatus
{
    Saved,
    Invalid,
    NotFound,
    NotLatest
}

public sealed record EvidenceDecisionResult(EvidenceDecisionStatus Status, string? Error = null);

public interface IEvidenceReviewService
{
    /// <summary>The live check for one scope, set against the last recorded review of the same scope.</summary>
    Task<EvidenceCheckPage> BuildLiveAsync(Guid tenantId, EvidenceScope scope);

    /// <summary>Null when there is nothing to record (no work items in the scope).</summary>
    Task<EvidenceReview?> RecordAsync(Guid tenantId, EvidenceScope scope, EvidenceReviewActor actor);

    Task<IReadOnlyList<EvidenceReview>> GetHistoryAsync(Guid tenantId);

    Task<EvidenceReviewPage?> GetReviewAsync(Guid tenantId, Guid reviewKey, string? tenantName = null);

    Task<EvidenceDecisionResult> DecideAsync(Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
        Guid? ownerStaffKey, DateOnly? targetDate, string? note, EvidenceReviewActor actor);
}

/// <summary>
/// Turns the Evidence Check into a weekly review loop: record the check,
/// decide each finding, and see next week whether the decisions held. The
/// rules live in the pure <see cref="EvidenceReviewCalculator"/>; this class
/// loads, validates against the tenant's staff, persists and audits.
///
/// Reachable by delivery-reporting roles below Admin, so, like
/// EvidenceCheckService, it never reads StaffRate or any cost figure.
/// </summary>
public sealed class EvidenceReviewService(
    IEvidenceCheckService evidenceCheck,
    IEvidenceReviewRepository reviews,
    IStaffRepository staff,
    IAuditLogRepository audit,
    TimeProvider clock) : IEvidenceReviewService
{
    public const int HistoryShown = 52;

    public async Task<EvidenceCheckPage> BuildLiveAsync(Guid tenantId, EvidenceScope scope)
    {
        var report = await evidenceCheck.BuildAsync(tenantId, scope);
        var last = await reviews.GetLatestAsync(tenantId, scope.Key);
        EvidenceReviewComparison? comparison = null;
        if (last is not null && report.Readiness != EvidenceReadiness.NoData)
        {
            // An unsaved recording shows what "Record this check" would carry forward.
            var draft = EvidenceReviewCalculator.Record(report, last, tenantId, null, Guid.Empty);
            comparison = EvidenceReviewCalculator.Compare(draft, last, Today());
        }

        return new EvidenceCheckPage(report, last, comparison, await StaffNamesAsync(tenantId));
    }

    public async Task<EvidenceReview?> RecordAsync(Guid tenantId, EvidenceScope scope, EvidenceReviewActor actor)
    {
        var report = await evidenceCheck.BuildAsync(tenantId, scope);
        if (report.Readiness == EvidenceReadiness.NoData)
            return null;

        var previous = await reviews.GetLatestAsync(tenantId, scope.Key);
        var review = EvidenceReviewCalculator.Record(report, previous, tenantId, actor.StaffKey, Guid.NewGuid());
        await reviews.AddAsync(review);
        await audit.LogAsync("EvidenceReview", review.ReviewKey.ToString(), "Recorded", actor.MemberId,
            JsonSerializer.Serialize(new
            {
                readiness = review.Readiness.ToString(),
                findings = review.Findings.Count,
                carriedForward = review.Findings.Count(f => f.CarriedForward),
                previousReviewKey = previous?.ReviewKey,
                scope = scope.Key,
                periodFrom = scope.PeriodFrom,
                periodTo = scope.PeriodTo,
                extractedOn = scope.ExtractedOn
            }),
            clock.GetUtcNow().UtcDateTime, tenantId);
        return review;
    }

    public Task<IReadOnlyList<EvidenceReview>> GetHistoryAsync(Guid tenantId) => reviews.GetHistoryAsync(tenantId, HistoryShown);

    public async Task<EvidenceReviewPage?> GetReviewAsync(Guid tenantId, Guid reviewKey, string? tenantName = null)
    {
        var review = await reviews.GetAsync(tenantId, reviewKey);
        if (review is null)
            return null;

        var previous = await reviews.GetPreviousAsync(tenantId, reviewKey);
        var latest = await reviews.GetLatestAsync(tenantId, review.ScopeKey);
        var comparison = previous is null
            ? null
            : EvidenceReviewCalculator.Compare(review, previous, DateOnly.FromDateTime(review.RecordedAtUtc));
        var members = await staff.GetByTenantAsync(tenantId);
        return new EvidenceReviewPage(
            review, comparison, latest?.ReviewKey == reviewKey,
            members.ToDictionary(s => s.StaffKey, s => s.FullName),
            members.Where(s => s.IsActive).OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new StaffOption(s.StaffKey, s.FullName)).ToList(),
            tenantName);
    }

    public async Task<EvidenceDecisionResult> DecideAsync(Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
        Guid? ownerStaffKey, DateOnly? targetDate, string? note, EvidenceReviewActor actor)
    {
        var invalid = EvidenceReviewCalculator.Validate(disposition, ownerStaffKey, targetDate, note);
        if (invalid is not null)
            return new(EvidenceDecisionStatus.Invalid, invalid);
        if (targetDate is { } target && target < Today())
            return new(EvidenceDecisionStatus.Invalid, "The target date is in the past. Set the date the fix is now expected.");

        if (ownerStaffKey is { } owner)
        {
            // Tenant-scoped: an owner key from another tenant is refused exactly like an unknown one.
            var candidate = (await staff.GetByTenantAsync(tenantId)).FirstOrDefault(s => s.StaffKey == owner);
            if (candidate is null || !candidate.IsActive)
                return new(EvidenceDecisionStatus.Invalid, "The owner must be an active member of staff in this organisation.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var outcome = await reviews.DecideAsync(tenantId, reviewKey, findingKey, disposition, ownerStaffKey, targetDate, note, actor.StaffKey, now);
        if (outcome == DecisionWriteOutcome.NotLatest)
            return new(EvidenceDecisionStatus.NotLatest,
                "A later check has been recorded since this one, so this review is now history. Record decisions on the latest review.");
        if (outcome == DecisionWriteOutcome.NotFound)
            return new(EvidenceDecisionStatus.NotFound);

        await audit.LogAsync("EvidenceReviewFinding", $"{reviewKey}/{findingKey}", "Decided", actor.MemberId,
            JsonSerializer.Serialize(new { disposition = disposition.ToString(), ownerStaffKey, targetDate }), now, tenantId);
        return new(EvidenceDecisionStatus.Saved);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> StaffNamesAsync(Guid tenantId) =>
        (await staff.GetByTenantAsync(tenantId)).ToDictionary(s => s.StaffKey, s => s.FullName);

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
