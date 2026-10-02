using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>Evidence gaps decide whether a report can be trusted; delivery exceptions are trusted facts that need an owner.</summary>
public enum EvidenceFindingCategory
{
    EvidenceGap,
    DeliveryException
}

public enum EvidenceFindingSeverity
{
    High,
    Medium
}

/// <summary>
/// Whether a figure built on this data can support a decision. A band, never
/// a percentage: the thresholds are stated in <see cref="EvidenceCheckCalculator"/>
/// and a reader can check them against the numerators shown, which a blended
/// score would hide.
/// </summary>
public enum EvidenceReadiness
{
    DecisionReady,
    UseWithCaveats,
    NotDecisionReady,
    NoData
}

/// <summary>One source record behind a finding — enough to find it in the source tool, no person named.</summary>
public sealed record EvidenceRecord(string Project, string? Source, string? ExternalId, string Title, string Detail);

/// <summary>
/// One line of the exception register. Numerator and denominator are always
/// shown together ("12 of 80 committed items"), matching the per-finding
/// fields docs/commercial/evidence-pack-template.md requires.
/// </summary>
public sealed record EvidenceFinding(
    string Key,
    EvidenceFindingCategory Category,
    EvidenceFindingSeverity Severity,
    string Title,
    string WhyItMatters,
    decimal Numerator,
    decimal Denominator,
    string Unit,
    IReadOnlyList<EvidenceRecord> Records)
{
    public bool IsMaterial => Denominator > 0 && Numerator / Denominator >= EvidenceCheckCalculator.MaterialShare;
}

public sealed record ProjectEvidenceSummary(string Project, EvidenceReadiness Readiness, int OpenItems, int EvidenceGaps, int DeliveryExceptions);

/// <summary>
/// What a check is about (GTM review 28 Sept, finding 1): which programme or
/// customer, which reporting period, when the data was extracted, and the
/// decision the review supports. A review is compared only with earlier
/// reviews of the same <see cref="Key"/>; the period moves each cycle, so it
/// isn't part of the key.
/// </summary>
public sealed record EvidenceScope(
    Guid? ProgrammeKey,
    Guid? CustomerKey,
    string Label,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly? ExtractedOn = null,
    string? Decision = null)
{
    public const int MaxDecisionLength = 500;
    public const int DefaultPeriodDays = 7;

    /// <summary>Also the key of every review recorded before scopes existed, which read the whole organisation.</summary>
    public const string WholeOrganisationKey = "p:all|c:all";

    public string Key => KeyFor(ProgrammeKey, CustomerKey);

    public bool IsWholeOrganisation => ProgrammeKey is null && CustomerKey is null;

    public static string KeyFor(Guid? programmeKey, Guid? customerKey) =>
        $"p:{programmeKey?.ToString("N") ?? "all"}|c:{customerKey?.ToString("N") ?? "all"}";
}

public sealed record EvidenceCheckReport(
    DateTime AsOfUtc,
    EvidenceReadiness Readiness,
    string ReadinessReason,
    int WorkItems,
    decimal RecordedHours,
    IReadOnlyList<EvidenceFinding> Findings,
    IReadOnlyList<ProjectEvidenceSummary> Projects,
    IReadOnlyList<string> SourceNotes,
    EvidenceScope? Scope = null);

/// <summary>
/// Everything the check reads. Silver only, plus source run state — never StaffRate or any cost field.
/// With a <see cref="Scope"/>, <see cref="WorkItems"/> are the scope's items and
/// <see cref="TimeEntries"/> the scope's time within its period;
/// <see cref="RecordedHoursByWorkItem"/> is all time ever recorded against each
/// item (estimate accuracy is cumulative), defaulting to the entries given; and
/// <see cref="HoursOutsideScope"/> is period time that no work item places in the scope.
/// </summary>
public sealed record EvidenceCheckInput(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<Workstream> Workstreams,
    IReadOnlyList<WorkItem> WorkItems,
    IReadOnlyList<TimeEntry> TimeEntries,
    int UnresolvedPeople,
    IReadOnlyList<SourceRunState> Sources,
    EvidenceScope? Scope = null,
    IReadOnlyDictionary<Guid, decimal>? RecordedHoursByWorkItem = null,
    decimal HoursOutsideScope = 0);

/// <summary>Source run state, reduced to what the check needs.</summary>
public sealed record SourceRunState(string DisplayName, DateTime? LastPublishedAtUtc, bool LastRunFailed, bool IsRunning);

/// <summary>
/// The automated exception register: what a paid diagnostic previously
/// assembled by hand (docs/commercial/paid-diagnostic-offer.md). Pure and
/// deterministic, so every rule below is unit-tested directly.
///
/// Readiness, stated so a reader can verify it:
/// - <b>NoData</b> — no work items at all.
/// - <b>NotDecisionReady</b> — any High-severity evidence gap covers at least
///   <see cref="MaterialShare"/> of its denominator, or a connected source's
///   last run failed or has never published.
/// - <b>UseWithCaveats</b> — any other evidence gap exists, or a source last
///   published more than <see cref="StaleAfterDays"/> days ago. A weekly
///   review can't call last month's data decision-ready however clean it
///   is, so staleness caps the headline rather than sitting in a note.
/// - <b>DecisionReady</b> — no evidence gaps and every source current. Delivery exceptions (overdue,
///   blocked, over estimate) never lower readiness: they are well-evidenced
///   bad news, which is exactly what a trusted report should show.
/// </summary>
public static class EvidenceCheckCalculator
{
    public const decimal MaterialShare = 0.10m;
    public const int StaleAfterDays = 7;
    private const string NoProject = "(not linked to a project)";

    private static readonly WorkItemLifecycleStage[] Committed =
        [WorkItemLifecycleStage.Ready, WorkItemLifecycleStage.InProgress, WorkItemLifecycleStage.Blocked, WorkItemLifecycleStage.InReview];

    public static EvidenceCheckReport Evaluate(EvidenceCheckInput input, DateTime asOfUtc)
    {
        var projectNames = input.Projects.ToDictionary(p => p.ProjectKey, p => p.Name);
        var projectOfWorkstream = input.Workstreams.ToDictionary(w => w.WorkstreamKey, w => w.ProjectKey);
        string ProjectOf(WorkItem item) =>
            projectOfWorkstream.TryGetValue(item.WorkstreamKey, out var projectKey) && projectNames.TryGetValue(projectKey, out var name)
                ? name : NoProject;

        var items = input.WorkItems;
        var itemsByKey = items.ToDictionary(i => i.WorkItemKey);
        var open = items.Where(IsOpen).ToList();
        var committed = items.Where(i => Committed.Contains(i.Stage)).ToList();
        var done = items.Where(i => i.Stage == WorkItemLifecycleStage.Done).ToList();
        var hoursByItem = input.TimeEntries.Where(t => t.WorkItemKey is not null)
            .GroupBy(t => t.WorkItemKey!.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.DurationHours));
        // Estimate accuracy is cumulative, so it reads all recorded time, not the period's.
        var recordedByItem = input.RecordedHoursByWorkItem ?? hoursByItem;
        decimal Recorded(WorkItem item) => recordedByItem.GetValueOrDefault(item.WorkItemKey);
        var totalHours = input.TimeEntries.Sum(t => t.DurationHours);
        var today = asOfUtc.Date;
        var scope = input.Scope;

        EvidenceRecord Item(WorkItem item, string detail) =>
            new(ProjectOf(item), item.ExternalSource, item.ExternalId, item.Title, detail);
        EvidenceRecord Time(TimeEntry entry, string detail)
        {
            var item = entry.WorkItemKey is { } key && itemsByKey.TryGetValue(key, out var found) ? found : null;
            var when = entry.WorkDate?.ToString("yyyy-MM-dd") ?? entry.StartedAtUtc?.ToString("yyyy-MM-dd") ?? "undated";
            return new(item is null ? NoProject : ProjectOf(item), entry.ExternalSource, entry.ExternalId,
                item?.Title ?? "Time entry", $"{entry.DurationHours:0.##} h on {when}. {detail}");
        }

        var findings = new List<EvidenceFinding>();

        void Add(string key, EvidenceFindingCategory category, EvidenceFindingSeverity severity, string title, string why,
            decimal numerator, decimal denominator, string unit, IEnumerable<EvidenceRecord> records)
        {
            if (numerator > 0)
                findings.Add(new(key, category, severity, title, why, numerator, denominator, unit, records.ToList()));
        }

        // ---- Evidence gaps: these decide whether the report can be trusted.

        var unmapped = items.Where(i => i.Stage == WorkItemLifecycleStage.Unmapped).ToList();
        Add("unmapped-status", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High,
            "Status can't be read as a lifecycle stage",
            "Progress, completion and overdue counts silently exclude these items, so every status total understates or overstates delivery.",
            unmapped.Count, items.Count, "items", unmapped.Select(i => Item(i, $"Source status \"{i.RawStatus ?? "(blank)"}\".")));

        var unowned = committed.Where(i => i.AssignedStaffKey is null).ToList();
        Add("committed-without-owner", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High,
            "Committed work with no matched owner",
            "Nobody accountable can be named, and capacity figures omit this work. Either no one is assigned in the source, or the assignee isn't matched to a staff profile.",
            unowned.Count, committed.Count, "committed items", unowned.Select(i => Item(i, $"Stage: {i.Stage.ToDisplayLabel()}.")));

        var noEstimate = committed.Where(i => i.EstimatedHours is null).ToList();
        Add("committed-without-estimate", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
            "Committed work with no estimate",
            "Forecasts and estimate-versus-actual comparisons can't include these items, so remaining effort is understated.",
            noEstimate.Count, committed.Count, "committed items", noEstimate.Select(i => Item(i, $"Stage: {i.Stage.ToDisplayLabel()}.")));

        var noDue = committed.Where(i => i.DueDateUtc is null).ToList();
        Add("committed-without-due-date", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
            "Committed work with no due date",
            "These items can never show as late, so the overdue count is a floor rather than a fact.",
            noDue.Count, committed.Count, "committed items", noDue.Select(i => Item(i, $"Stage: {i.Stage.ToDisplayLabel()}.")));

        if (input.TimeEntries.Count == 0)
        {
            if (items.Count > 0)
                // Medium, not High: a status report with no time data is common
                // and its delivery facts still stand; only effort and cost
                // can't be evidenced, and the finding says so.
                findings.Add(new("no-time-evidence", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
                    scope is null ? "No recorded time at all" : "No recorded time in this period",
                    "Effort, cost and estimate accuracy can't be evidenced. Any cost or utilisation figure in the report comes from somewhere this check can't see.",
                    items.Count, items.Count, "items without time evidence", []));
        }
        else
        {
            var unlinked = input.TimeEntries.Where(t => t.WorkItemKey is null).ToList();
            Add("time-not-linked", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High,
                "Recorded time not linked to any work item",
                "These hours are cost with no delivery attached. Per-project and per-item effort figures exclude them.",
                unlinked.Sum(t => t.DurationHours), totalHours, "hours", unlinked.Select(t => Time(t, "No work item.")));

            var unattributed = input.TimeEntries.Where(t => t.StaffKey is null).ToList();
            Add("time-without-person", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.High,
                "Recorded time with no matched person",
                "These hours can't be costed or counted against anyone's capacity until the person is matched on the identity queue.",
                unattributed.Sum(t => t.DurationHours), totalHours, "hours", unattributed.Select(t => Time(t, "Person not matched.")));

            var unknownBillability = input.TimeEntries.Where(t => !t.BillabilityKnown).ToList();
            Add("billability-unknown", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
                "Hours with unknown billability",
                "Utilisation and recoverable-revenue figures have to guess for these hours.",
                unknownBillability.Sum(t => t.DurationHours), totalHours, "hours", unknownBillability.Select(t => Time(t, "Billable not recorded.")));

            // Only for sources that record time at all: a Jira project with no
            // Tempo connection would otherwise flag every finished issue.
            // Finished work is judged on all its recorded time: work done
            // before the period isn't "free" because none fell inside it.
            var sourcesWithTime = items.Where(i => Recorded(i) > 0).Select(i => i.ExternalSource).ToHashSet();
            var doneInTimedSources = done.Where(i => sourcesWithTime.Contains(i.ExternalSource)).ToList();
            var doneNoTime = doneInTimedSources.Where(i => Recorded(i) == 0).ToList();
            Add("done-without-time", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
                "Finished work with no recorded time",
                "Either the work was free, which is unlikely, or its effort was booked elsewhere. Either way, cost per deliverable is understated.",
                doneNoTime.Count, doneInTimedSources.Count, "finished items", doneNoTime.Select(i => Item(i, "Done, 0 h recorded.")));
        }

        if (input.UnresolvedPeople > 0)
            findings.Add(new("unmatched-people", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium,
                "People seen in source data but not matched to staff",
                "Their work and time sit outside every capacity and cost figure until an Admin links them on the identity queue.",
                input.UnresolvedPeople, input.UnresolvedPeople, "people awaiting a match", []));

        var sourceNotes = new List<string>();
        var sourceProblems = 0;
        var staleSources = new List<string>();
        // Sentences that cap readiness at UseWithCaveats: the data isn't current for the period.
        var currency = new List<string>();
        foreach (var source in input.Sources)
        {
            if (source.LastRunFailed)
            {
                sourceProblems++;
                sourceNotes.Add($"{source.DisplayName}: the last sync failed, so figures may mix old and partial data.");
            }
            else if (source.IsRunning)
            {
                sourceNotes.Add($"{source.DisplayName}: a sync is running, so figures may change while you read them.");
            }
            else if (source.LastPublishedAtUtc is null)
            {
                sourceProblems++;
                sourceNotes.Add($"{source.DisplayName}: connected but has never published.");
            }
            else if ((asOfUtc - source.LastPublishedAtUtc.Value).TotalDays > StaleAfterDays)
            {
                staleSources.Add(source.DisplayName);
                sourceNotes.Add($"{source.DisplayName}: last published {source.LastPublishedAtUtc:yyyy-MM-dd}, more than {StaleAfterDays} days ago.");
            }
        }
        if (staleSources.Count > 0)
            currency.Add($"{string.Join(", ", staleSources)} last published more than {StaleAfterDays} days ago, so figures may not reflect this week");

        // A declared extract date is what the data's age is judged on: an old
        // export uploaded today is still old.
        if (scope?.ExtractedOn is { } extracted)
        {
            if (DateOnly.FromDateTime(today).DayNumber - extracted.DayNumber > StaleAfterDays)
                currency.Add($"The data was extracted on {extracted:yyyy-MM-dd}, more than {StaleAfterDays} days ago");
            else if (extracted < scope.PeriodTo)
                currency.Add($"The data was extracted on {extracted:yyyy-MM-dd}, before the period ends on {scope.PeriodTo:yyyy-MM-dd}, so its last days are missing");
        }

        if (input.HoursOutsideScope > 0)
            sourceNotes.Add($"{input.HoursOutsideScope:0.##} hours recorded in the period aren't linked to any work item, so nothing places them in this scope and they are left out of it. The whole-organisation check includes them.");

        // ---- Delivery exceptions: trusted facts that need an owner and a decision.

        var overdue = open.Where(i => i.IsOverdueOn(asOfUtc)).ToList();
        Add("overdue", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.High,
            "Open work past its due date",
            "Commitments already missed. Each needs a new date and an owner, or a decision to stop.",
            overdue.Count, open.Count, "open items",
            overdue.OrderBy(i => i.DueDateUtc).Select(i => Item(i, $"Due {i.DueDateUtc:yyyy-MM-dd}, {DaysLate(i.DaysPastDue(asOfUtc))}, stage: {i.Stage.ToDisplayLabel().ToLowerInvariant()}.")));

        var blocked = open.Where(i => i.Stage == WorkItemLifecycleStage.Blocked).ToList();
        Add("blocked", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.High,
            "Blocked work",
            "Progress here depends on a decision or dependency outside the team.",
            blocked.Count, open.Count, "open items", blocked.Select(i => Item(i, i.DueDateUtc is { } due ? $"Due {due:yyyy-MM-dd}." : "No due date.")));

        var overEstimate = items.Where(i => i.EstimatedHours is > 0 && Recorded(i) > i.EstimatedHours).ToList();
        var estimatedWithTime = items.Count(i => i.EstimatedHours is > 0 && Recorded(i) > 0);
        Add("over-estimate", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium,
            "Work that has already used more than its estimate",
            "On fixed-price work this is margin being spent. On time-and-materials work it's a conversation the client hasn't had yet."
                + (scope is null ? "" : " Measured on all time ever recorded against the item, not just this period."),
            overEstimate.Count, estimatedWithTime, "estimated items with time",
            overEstimate.OrderByDescending(i => Recorded(i) / i.EstimatedHours!.Value)
                .Select(i => Item(i, $"{Recorded(i):0.##} h against {i.EstimatedHours:0.##} h estimate ({Recorded(i) / i.EstimatedHours!.Value:P0}).")));

        var backlogWithTime = items.Where(i => i.Stage == WorkItemLifecycleStage.Backlog && hoursByItem.ContainsKey(i.WorkItemKey)).ToList();
        Add("time-on-uncommitted-work", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.Medium,
            "Time spent on work still in the backlog",
            "Effort is going into work nobody has committed to. Either the status is out of date, or unplanned work is consuming capacity.",
            backlogWithTime.Sum(i => hoursByItem[i.WorkItemKey]), totalHours, "hours",
            backlogWithTime.Select(i => Item(i, $"{hoursByItem[i.WorkItemKey]:0.##} h recorded while in Backlog.")));

        var ordered = findings
            .OrderBy(f => f.Category)
            .ThenBy(f => f.Severity)
            .ThenByDescending(f => f.Denominator == 0 ? 0 : f.Numerator / f.Denominator)
            .ToList();

        var (readiness, reason) = Decide(items.Count, ordered.Where(f => f.Category == EvidenceFindingCategory.EvidenceGap).ToList(), sourceProblems, currency);

        var projects = items.GroupBy(ProjectOf)
            .Select(group =>
            {
                var gaps = ordered.Where(f => f.Category == EvidenceFindingCategory.EvidenceGap)
                    .SelectMany(f => f.Records).Count(r => r.Project == group.Key);
                var exceptions = ordered.Where(f => f.Category == EvidenceFindingCategory.DeliveryException)
                    .SelectMany(f => f.Records).Count(r => r.Project == group.Key);
                var projectGaps = ordered.Where(f => f.Category == EvidenceFindingCategory.EvidenceGap)
                    .Select(f => f with { Records = f.Records.Where(r => r.Project == group.Key).ToList() })
                    .Where(f => f.Records.Count > 0)
                    .ToList();
                var projectItems = group.ToList();
                var projectReadiness = projectGaps.Count == 0 ? EvidenceReadiness.DecisionReady
                    : projectGaps.Any(f => f.Severity == EvidenceFindingSeverity.High && f.Unit.EndsWith("items", StringComparison.Ordinal)
                        && f.Records.Count >= Math.Max(1m, projectItems.Count * MaterialShare))
                        ? EvidenceReadiness.NotDecisionReady
                        : EvidenceReadiness.UseWithCaveats;
                return new ProjectEvidenceSummary(group.Key, projectReadiness, projectItems.Count(IsOpen), gaps, exceptions);
            })
            .OrderByDescending(p => p.Readiness == EvidenceReadiness.NotDecisionReady)
            .ThenByDescending(p => p.EvidenceGaps)
            .ThenBy(p => p.Project, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EvidenceCheckReport(asOfUtc, readiness, reason, items.Count, totalHours, ordered, projects, sourceNotes, scope);
    }

    private static (EvidenceReadiness, string) Decide(int workItems, List<EvidenceFinding> gaps, int sourceProblems, List<string> currency)
    {
        if (workItems == 0)
            return (EvidenceReadiness.NoData, "No work items yet. Import a work-items file or run a sync first.");
        if (sourceProblems > 0)
            return (EvidenceReadiness.NotDecisionReady, "A connected source failed or has never published, so current figures may be partial.");

        var material = gaps.Where(f => f.Severity == EvidenceFindingSeverity.High && f.IsMaterial).ToList();
        if (material.Count > 0)
            return (EvidenceReadiness.NotDecisionReady,
                $"{material.Count} high-severity evidence gap{(material.Count == 1 ? "" : "s")} each affect at least {MaterialShare:P0} of the data: {string.Join("; ", material.Select(f => f.Title.ToLowerInvariant()))}.");
        var stale = string.Concat(currency.Select(sentence => $" {sentence}."));
        if (gaps.Count > 0)
            return (EvidenceReadiness.UseWithCaveats,
                $"{gaps.Count} evidence gap{(gaps.Count == 1 ? "" : "s")}, none affecting {MaterialShare:P0} or more of its data. State them alongside the figures.{stale}");
        if (currency.Count > 0)
            return (EvidenceReadiness.UseWithCaveats,
                $"No evidence gaps found, but the data is not current.{stale} Refresh it, or state its date with the figures.");
        return (EvidenceReadiness.DecisionReady, "No evidence gaps found. Delivery exceptions below are well-evidenced and need owners.");
    }

    private static bool IsOpen(WorkItem item) =>
        item.Stage is not (WorkItemLifecycleStage.Done or WorkItemLifecycleStage.Cancelled);

    private static string DaysLate(int days) => days == 1 ? "1 day late" : $"{days} days late";
}
