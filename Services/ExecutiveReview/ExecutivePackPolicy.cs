using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ExecutiveReview;

public static class ExecutivePackPolicy
{
    public static ExecutiveMetrics Summarize(IReadOnlyList<WorkItemEvidence> items, DateTime capturedAtUtc)
    {
        static bool Open(WorkItemEvidence item) => item.Stage is not (WorkItemLifecycleStage.Done or WorkItemLifecycleStage.Cancelled);
        return new(items.Count, items.Count(Open), items.Count(i => i.Stage == WorkItemLifecycleStage.Blocked),
            items.Count(i => i.Stage == WorkItemLifecycleStage.Unmapped), items.Count(i => Open(i) && i.DueAtUtc is null),
            items.Count(i => Open(i) && i.DueAtUtc < capturedAtUtc));
    }

    public static void RequirePublication(SyncRun? run, DateTime now, int maximumAgeHours)
    {
        if (maximumAgeHours is < 1 or > 168) throw new ReviewValidationException("Review.InvalidFreshness");
        if (run?.Status != SyncRunStatus.Succeeded || run.FinishedAtUtc is not DateTime published
            || published < run.StartedAtUtc || published > now
            || now - published > TimeSpan.FromHours(maximumAgeHours))
            throw new ReviewValidationException("Review.PublicationRequired");
    }

    public static IReadOnlyList<WorkItemEvidence> Evidence(IEnumerable<WorkItem> items, Guid tenantId, string source)
    {
        var result = new List<WorkItemEvidence>();
        foreach (var item in items.Where(i => string.Equals(i.ExternalSource, source, StringComparison.Ordinal)))
        {
            if (item.TenantId != tenantId || string.IsNullOrWhiteSpace(item.ExternalId))
                throw new ReviewValidationException("Review.ProvenanceRequired");
            result.Add(new(item.WorkItemKey, item.ExternalId, item.Stage,
                item.DueDateUtc is DateTime due ? DateTime.SpecifyKind(due, DateTimeKind.Utc) : null,
                item.IsMilestone, DateTime.SpecifyKind(item.UpdatedAtUtc, DateTimeKind.Utc)));
        }
        if (result.Count == 0) throw new ReviewValidationException("Review.NoEvidence");
        return result.OrderBy(i => i.WorkItemKey).ToArray();
    }
}
