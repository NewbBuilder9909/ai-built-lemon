using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>Which of the two sources the tenant has data from. Neither means the report doesn't exist.</summary>
public enum ReconciliationShape
{
    JiraAndTempo,
    JiraOnly,
    TempoOnly
}

/// <summary>
/// Where the period's Tempo hours went. The parts always add up to
/// <see cref="Total"/> (<see cref="Linked"/> + <see cref="IssueNotSynced"/>
/// + <see cref="NoIssue"/>), so none can go missing between the headline
/// and the rows.
/// </summary>
public sealed record TempoHoursSummary(
    int Worklogs,
    decimal Total,
    decimal Linked,
    decimal IssueNotSynced,
    decimal NoIssue,
    decimal WithoutPerson);

/// <summary>
/// One synced Jira project. Hours in the period are Tempo's (Jira worklogs
/// are never imported, so nothing is counted twice). Estimate figures use
/// all recorded time, because variance is cumulative.
/// </summary>
public sealed record JiraProjectReconciliation(
    string Project,
    int Issues,
    int OpenIssues,
    decimal PeriodHours,
    int IssuesWithPeriodTime,
    int? OpenIssuesWithNoTimeRecorded,
    int EstimatedIssuesWithTime,
    int OverEstimate);

/// <summary>Hours logged in Tempo against a Jira issue that isn't among the synced projects.</summary>
public sealed record UnlinkedIssueHours(string IssueId, int Worklogs, decimal Hours);

public sealed record JiraTempoReconciliationReport(
    ReconciliationShape Shape,
    DateOnly From,
    DateOnly To,
    SourceRunState? Jira,
    SourceRunState? Tempo,
    TempoHoursSummary? Hours,
    IReadOnlyList<JiraProjectReconciliation> Projects,
    IReadOnlyList<UnlinkedIssueHours> UnlinkedIssues,
    int MoreUnlinkedIssues,
    decimal MoreUnlinkedHours,
    IReadOnlyList<string> Notes)
{
    public bool HasJira => Shape is ReconciliationShape.JiraAndTempo or ReconciliationShape.JiraOnly;

    public bool HasTempo => Shape is ReconciliationShape.JiraAndTempo or ReconciliationShape.TempoOnly;
}

/// <summary>Everything the reconciliation reads. Silver and source run state only, never StaffRate or cost.</summary>
public sealed record JiraTempoReconciliationInput(
    DateOnly From,
    DateOnly To,
    SourceDataPresence JiraData,
    SourceDataPresence TempoData,
    SourceRunState? JiraRun,
    SourceRunState? TempoRun,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<Workstream> Workstreams,
    IReadOnlyList<WorkItem> WorkItems,
    IReadOnlyList<TimeEntry> PeriodTimeEntries,
    IReadOnlyDictionary<Guid, decimal> RecordedHoursByWorkItem);

/// <summary>
/// Jira issues set against Tempo worklogs, for tenants that have either or
/// both (docs/jira-tempo-integration-scope.md, phase 3). Pure, so each rule
/// below is unit-tested directly.
///
/// - The report exists only when the tenant has data from at least one of
///   the two sources, and its sections follow what is there: no Tempo means
///   no hours section rather than a row of zeros, and no Jira means every
///   hour is shown as unlinked, with the reason.
/// - Tempo is the time authority. Jira worklogs are never imported, so an
///   hour is counted once.
/// - A Tempo hour is <b>linked</b> when its issue is a synced Jira work item,
///   <b>issue not synced</b> when Tempo named an issue outside the synced
///   projects, and <b>no issue</b> otherwise. The three always sum to the
///   total.
/// - A source whose last run failed, or that never published, is stated at
///   the top. Its figures are never presented as a healthy zero.
/// - Nothing here names a person; unmatched people appear only as hours.
/// </summary>
public static class JiraTempoReconciliationCalculator
{
    public const string JiraSource = "Jira";
    public const string TempoSource = "Tempo";
    public const int UnlinkedIssuesShown = 25;

    private const string NoProject = "(not linked to a project)";

    /// <summary>Null when the tenant has no Jira or Tempo data: there is nothing to reconcile, so no page.</summary>
    public static JiraTempoReconciliationReport? Evaluate(JiraTempoReconciliationInput input)
    {
        var hasJira = input.JiraData.Any;
        var hasTempo = input.TempoData.Any;
        if (!hasJira && !hasTempo)
            return null;
        var shape = hasJira && hasTempo ? ReconciliationShape.JiraAndTempo
            : hasJira ? ReconciliationShape.JiraOnly : ReconciliationShape.TempoOnly;

        var jiraItems = input.WorkItems.Where(i => i.ExternalSource == JiraSource).ToList();
        var jiraKeys = jiraItems.Select(i => i.WorkItemKey).ToHashSet();
        var tempo = input.PeriodTimeEntries.Where(t => t.ExternalSource == TempoSource).ToList();
        var notes = new List<string>();

        TempoHoursSummary? hours = null;
        List<UnlinkedIssueHours> unlinked = [];
        if (hasTempo)
        {
            bool Linked(TimeEntry t) => t.WorkItemKey is { } key && jiraKeys.Contains(key);
            var issueNotSynced = tempo.Where(t => !Linked(t) && !string.IsNullOrEmpty(t.SourceWorkItemExternalId)).ToList();
            hours = new TempoHoursSummary(
                tempo.Count,
                tempo.Sum(t => t.DurationHours),
                tempo.Where(Linked).Sum(t => t.DurationHours),
                issueNotSynced.Sum(t => t.DurationHours),
                tempo.Where(t => !Linked(t) && string.IsNullOrEmpty(t.SourceWorkItemExternalId)).Sum(t => t.DurationHours),
                tempo.Where(t => t.StaffKey is null).Sum(t => t.DurationHours));
            unlinked = issueNotSynced
                .GroupBy(t => t.SourceWorkItemExternalId!, StringComparer.Ordinal)
                .Select(g => new UnlinkedIssueHours(IssueIdOf(g.Key), g.Count(), g.Sum(t => t.DurationHours)))
                .OrderByDescending(u => u.Hours)
                .ThenBy(u => u.IssueId, StringComparer.Ordinal)
                .ToList();

            if (hours.NoIssue > 0)
                notes.Add($"{hours.NoIssue:0.##} hours have no Jira issue recorded. Worklogs synced before issue ids were kept show here until the next Tempo sync.");
            if (hours.WithoutPerson > 0)
                notes.Add($"{hours.WithoutPerson:0.##} hours are from people not yet matched to staff. Match them on the identity queue.");
            notes.Add("Tempo billability and approval are not confirmed, so these are recorded hours, not billable hours.");
            notes.Add("Tempo deletions are only verified inside the audited window, so older totals may include worklogs deleted in Tempo.");
        }

        List<JiraProjectReconciliation> projects = [];
        if (hasJira)
        {
            var projectNames = input.Projects.ToDictionary(p => p.ProjectKey, p => p.Name);
            var projectOfWorkstream = input.Workstreams.ToDictionary(w => w.WorkstreamKey, w => w.ProjectKey);
            string ProjectOf(WorkItem item) =>
                projectOfWorkstream.TryGetValue(item.WorkstreamKey, out var projectKey) && projectNames.TryGetValue(projectKey, out var name)
                    ? name : NoProject;

            var periodHoursByItem = input.PeriodTimeEntries
                .Where(t => t.WorkItemKey is { } key && jiraKeys.Contains(key))
                .GroupBy(t => t.WorkItemKey!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.DurationHours));
            decimal Recorded(WorkItem item) => input.RecordedHoursByWorkItem.GetValueOrDefault(item.WorkItemKey);

            projects = jiraItems.GroupBy(ProjectOf)
                .Select(group =>
                {
                    var items = group.ToList();
                    var open = items.Where(IsOpen).ToList();
                    var estimatedWithTime = items.Where(i => i.EstimatedHours is > 0 && Recorded(i) > 0).ToList();
                    return new JiraProjectReconciliation(
                        group.Key,
                        items.Count,
                        open.Count,
                        items.Sum(i => periodHoursByItem.GetValueOrDefault(i.WorkItemKey)),
                        items.Count(i => periodHoursByItem.ContainsKey(i.WorkItemKey)),
                        // Without a time source "no time recorded" is a fact about the setup, not the work.
                        hasTempo ? open.Count(i => Recorded(i) == 0) : null,
                        estimatedWithTime.Count,
                        estimatedWithTime.Count(i => Recorded(i) > i.EstimatedHours));
                })
                .OrderByDescending(p => p.PeriodHours)
                .ThenBy(p => p.Project, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (shape == ReconciliationShape.JiraOnly)
            notes.Insert(0, "Tempo isn't connected, so no time is recorded against these issues. Effort and estimate accuracy can't be shown.");
        if (shape == ReconciliationShape.TempoOnly)
            notes.Insert(0, input.JiraRun is null
                ? "Jira isn't connected, so no hours can be tied to an issue. The issues below are named by Tempo but their projects aren't synced."
                : "Jira is connected for identity only (no projects selected), so no hours can be tied to an issue. Add projects to the Jira connection to link them.");
        if (hasJira && hasTempo && unlinked.Count > 0)
            notes.Add("Hours against issues outside the synced Jira projects link automatically once those projects are added to the Jira connection and both sources sync again.");

        foreach (var (run, label) in new[] { (input.JiraRun, "Jira"), (input.TempoRun, "Tempo") })
        {
            if (run is null)
                continue;
            if (run.LastRunFailed)
                notes.Insert(0, $"{run.DisplayName}: the last sync failed. Figures below may be partial; don't read a missing {label} figure as zero.");
            else if (run.LastPublishedAtUtc is null)
                notes.Insert(0, $"{run.DisplayName}: has never published. Figures below may be partial.");
        }

        return new JiraTempoReconciliationReport(
            shape, input.From, input.To, input.JiraRun, input.TempoRun, hours, projects,
            unlinked.Take(UnlinkedIssuesShown).ToList(),
            Math.Max(0, unlinked.Count - UnlinkedIssuesShown),
            unlinked.Skip(UnlinkedIssuesShown).Sum(u => u.Hours),
            notes);
    }

    /// <summary>The Jira issue id from a "cloudId:issueId" external id, or the whole value if it has no prefix.</summary>
    public static string IssueIdOf(string sourceWorkItemExternalId)
    {
        var colon = sourceWorkItemExternalId.LastIndexOf(':');
        return colon < 0 ? sourceWorkItemExternalId : sourceWorkItemExternalId[(colon + 1)..];
    }

    private static bool IsOpen(WorkItem item) =>
        item.Stage is not (WorkItemLifecycleStage.Done or WorkItemLifecycleStage.Cancelled);
}
