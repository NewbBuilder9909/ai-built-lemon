using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>Why a project was flagged. A trigger decides that questions are asked, never what the cause is.</summary>
public enum DeliveryTrigger
{
    OverBaselineHours,
    Stalled,
    HoursExceedEstimateWithoutCompletion,
    OpenWorkPastDue
}

/// <summary>Evidence weight as a band with stated thresholds, never a percentage or a score.</summary>
public enum QuestionWeight
{
    Medium,
    High
}

/// <summary>The fixed set of answers an owner may give; the answer is the owner's, never inferred.</summary>
public enum DeliveryQuestionAnswer
{
    ScopeChange,
    EstimateError,
    Blocked,
    DataError,
    Other
}

/// <summary>One source record behind a question. No person is named.</summary>
public sealed record QuestionRecord(string? Source, string? ExternalId, string Title);

/// <summary>A question for the project owner, with the numerator, denominator and records it rests on.</summary>
public sealed record DeliveryQuestion(
    string Key,
    string Project,
    QuestionWeight Weight,
    string Question,
    decimal Numerator,
    decimal Denominator,
    string Unit,
    IReadOnlyList<QuestionRecord> Records);

public sealed record ProjectDeliveryQuestions(
    string Project,
    IReadOnlyList<DeliveryTrigger> Triggers,
    IReadOnlyList<DeliveryQuestion> Questions);

public sealed record DeliveryQuestionsInput(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<Workstream> Workstreams,
    IReadOnlyList<WorkItem> WorkItems,
    IReadOnlyList<TimeEntry> TimeEntries,
    IReadOnlyList<ChangeRequest> ChangeRequests,
    IReadOnlyList<WorkstreamBaseline> Baselines);

/// <summary>
/// Turns a stalled or over-budget project into a short list of questions for
/// its owner. Silver data only, and no cost or rate field is read, so any role
/// that can see the project can see this. Questions, never verdicts: a root
/// cause exists only when a named reviewer records one. People are never named
/// or ranked.
/// </summary>
public static class DeliveryQuestionsCalculator
{
    public const int StalledDays = 21;
    public const decimal PastDueShare = 0.25m;
    public const decimal OverEstimateShare = 0.5m;
    public const decimal LateAddedShare = 0.2m;
    public const decimal HighWeightShare = 0.3m;
    public const decimal ConcentrationShare = 0.6m;
    public const int MaxQuestionsPerProject = 5;
    private const int MaxRecords = 10;

    public static IReadOnlyList<ProjectDeliveryQuestions> Calculate(DeliveryQuestionsInput input, DateTime asOfUtc)
    {
        var result = new List<ProjectDeliveryQuestions>();
        foreach (var project in input.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var streams = input.Workstreams.Where(w => w.ProjectKey == project.ProjectKey).Select(w => w.WorkstreamKey).ToHashSet();
            var items = input.WorkItems.Where(i => streams.Contains(i.WorkstreamKey) && i.Stage != WorkItemLifecycleStage.Cancelled).ToList();
            var itemKeys = items.Select(i => i.WorkItemKey).ToHashSet();
            var time = input.TimeEntries.Where(t => t.WorkItemKey is { } k && itemKeys.Contains(k)).ToList();
            var baselines = input.Baselines.Where(b => streams.Contains(b.WorkstreamKey)).ToList();
            var changes = input.ChangeRequests.Where(c => c.ProjectKey == project.ProjectKey).ToList();

            var triggers = Triggers(items, time, baselines, asOfUtc);
            if (triggers.Count == 0) continue;

            var questions = new List<DeliveryQuestion>();
            AddIfAny(questions, StalledQuestion(project.Name, items, asOfUtc));
            AddIfAny(questions, LateAddedQuestion(project.Name, items, baselines, changes));
            AddIfAny(questions, OverEstimateQuestion(project.Name, items, time));
            AddIfAny(questions, UnestimatedQuestion(project.Name, items, time));
            AddIfAny(questions, ConcentrationQuestion(project.Name, time));

            var top = questions
                .OrderByDescending(q => q.Weight)
                .ThenByDescending(q => q.Numerator)
                .ThenBy(q => q.Key, StringComparer.Ordinal)
                .Take(MaxQuestionsPerProject)
                .ToList();
            result.Add(new ProjectDeliveryQuestions(project.Name, triggers, top));
        }
        return result;
    }

    /// <summary>Hours logged to each item, from the entries given.</summary>
    private static Dictionary<Guid, decimal> HoursByItem(IEnumerable<TimeEntry> time) =>
        time.Where(t => t.WorkItemKey is not null)
            .GroupBy(t => t.WorkItemKey!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationHours));

    private static bool IsOpen(WorkItem i) => i.Stage != WorkItemLifecycleStage.Done && i.Stage != WorkItemLifecycleStage.Cancelled;

    private static List<DeliveryTrigger> Triggers(List<WorkItem> items, List<TimeEntry> time, List<WorkstreamBaseline> baselines, DateTime asOf)
    {
        var triggers = new List<DeliveryTrigger>();
        var open = items.Where(IsOpen).ToList();
        var logged = time.Sum(t => t.DurationHours);

        var baselineHours = baselines.Sum(b => b.BaselineHours);
        if (baselineHours > 0 && logged > baselineHours) triggers.Add(DeliveryTrigger.OverBaselineHours);

        var stalled = open.Count > 0 && open.All(i => (asOf - i.UpdatedAtUtc).TotalDays >= StalledDays);
        if (stalled) triggers.Add(DeliveryTrigger.Stalled);

        var estimated = items.Where(i => i.EstimatedHours is > 0).ToList();
        var byItem = HoursByItem(time);
        var estimateTotal = estimated.Sum(i => i.EstimatedHours!.Value);
        var loggedOnEstimated = estimated.Sum(i => byItem.GetValueOrDefault(i.WorkItemKey));
        var noRecentCompletion = !items.Any(i => i.Stage == WorkItemLifecycleStage.Done && (asOf - i.UpdatedAtUtc).TotalDays < StalledDays);
        if (estimateTotal > 0 && loggedOnEstimated > estimateTotal && noRecentCompletion)
            triggers.Add(DeliveryTrigger.HoursExceedEstimateWithoutCompletion);

        var due = open.Where(i => i.DueDateUtc is not null).ToList();
        if (due.Count > 0 && (decimal)due.Count(i => i.DueDateUtc < asOf) / due.Count >= PastDueShare)
            triggers.Add(DeliveryTrigger.OpenWorkPastDue);

        return triggers;
    }

    private static void AddIfAny(List<DeliveryQuestion> list, DeliveryQuestion? q)
    {
        if (q is not null) list.Add(q);
    }

    private static QuestionWeight Weight(decimal numerator, decimal denominator) =>
        denominator > 0 && numerator / denominator >= HighWeightShare ? QuestionWeight.High : QuestionWeight.Medium;

    private static List<QuestionRecord> Records(IEnumerable<WorkItem> items) =>
        items.OrderBy(i => i.ExternalId, StringComparer.Ordinal).Take(MaxRecords)
            .Select(i => new QuestionRecord(i.ExternalSource, i.ExternalId, i.Title)).ToList();

    private static DeliveryQuestion? StalledQuestion(string project, List<WorkItem> items, DateTime asOf)
    {
        var open = items.Where(IsOpen).ToList();
        var stalled = open.Where(i => (asOf - i.UpdatedAtUtc).TotalDays >= StalledDays).ToList();
        if (stalled.Count == 0) return null;
        return new DeliveryQuestion("stalled", project, Weight(stalled.Count, open.Count),
            $"{stalled.Count} of {open.Count} open items have not changed in {StalledDays} days or more. What is blocking them, and who owns the unblocking?",
            stalled.Count, open.Count, "open items", Records(stalled));
    }

    private static DeliveryQuestion? LateAddedQuestion(string project, List<WorkItem> items, List<WorkstreamBaseline> baselines, List<ChangeRequest> changes)
    {
        if (baselines.Count == 0 || items.Count == 0) return null;
        var lockedAt = baselines.ToDictionary(b => b.WorkstreamKey, b => b.LockedAtUtc);
        var late = items.Where(i => lockedAt.TryGetValue(i.WorkstreamKey, out var at) && i.CreatedAtUtc > at).ToList();
        if (late.Count == 0 || (decimal)late.Count / items.Count < LateAddedShare) return null;
        var approved = changes.Count(c => c.Status is ChangeRequestStatus.Approved or ChangeRequestStatus.Implemented);
        var uncovered = Math.Max(0, late.Count - approved);
        if (uncovered == 0) return null;
        return new DeliveryQuestion("late-scope", project, Weight(late.Count, items.Count),
            $"{late.Count} of {items.Count} items were added after the baseline was locked, and only {approved} approved change request(s) exist. Who approved the rest, and was it a scope change?",
            late.Count, items.Count, "items", Records(late));
    }

    private static DeliveryQuestion? OverEstimateQuestion(string project, List<WorkItem> items, List<TimeEntry> time)
    {
        var byItem = HoursByItem(time);
        var estimated = items.Where(i => i.EstimatedHours is > 0).ToList();
        var over = estimated.Where(i => byItem.GetValueOrDefault(i.WorkItemKey) > i.EstimatedHours!.Value * (1 + OverEstimateShare)).ToList();
        if (over.Count == 0) return null;
        return new DeliveryQuestion("over-estimate", project, Weight(over.Count, estimated.Count),
            $"Logged hours on {over.Count} of {estimated.Count} estimated items exceed the estimate by more than 50%. Was the estimate wrong, or did the scope change?",
            over.Count, estimated.Count, "estimated items", Records(over));
    }

    private static DeliveryQuestion? UnestimatedQuestion(string project, List<WorkItem> items, List<TimeEntry> time)
    {
        var byItem = HoursByItem(time);
        var worked = items.Where(i => byItem.GetValueOrDefault(i.WorkItemKey) > 0).ToList();
        var unestimated = worked.Where(i => i.EstimatedHours is null or <= 0).ToList();
        if (unestimated.Count == 0) return null;
        return new DeliveryQuestion("unestimated-work", project, Weight(unestimated.Count, worked.Count),
            $"{unestimated.Count} of {worked.Count} items with logged time have no estimate. Was this work planned, and where did it come from?",
            unestimated.Count, worked.Count, "items with time", Records(unestimated));
    }

    private static DeliveryQuestion? ConcentrationQuestion(string project, List<TimeEntry> time)
    {
        var total = time.Sum(t => t.DurationHours);
        if (total <= 0) return null;
        var top = time.Where(t => t.StaffKey is not null).GroupBy(t => t.StaffKey).Select(g => g.Sum(t => t.DurationHours)).DefaultIfEmpty(0).Max();
        if (top / total < ConcentrationShare) return null;
        return new DeliveryQuestion("concentration", project, QuestionWeight.Medium,
            $"{Math.Round(top, 1)} of {Math.Round(total, 1)} logged hours sit with one person. What happens to delivery if they are unavailable?",
            top, total, "hours", []);
    }
}
