using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The Jira and Tempo report, against hand-built Silver data. The page must
/// exist only for a tenant that has one of the two sources and adapt to
/// which; each rule that decides that has a test that states it.
/// </summary>
public class JiraTempoReconciliationTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 28);
    private const string Cloud = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid Alex = Guid.NewGuid();

    private sealed class Data
    {
        public List<Project> Projects { get; } = [];
        public List<Workstream> Workstreams { get; } = [];
        public List<WorkItem> Items { get; } = [];
        public List<TimeEntry> Time { get; } = [];
        public Dictionary<Guid, decimal> Recorded { get; } = [];
        public SourceRunState? JiraRun { get; set; }
        public SourceRunState? TempoRun { get; set; }

        public WorkItem Issue(string id, string project = "Payments", WorkItemLifecycleStage stage = WorkItemLifecycleStage.InProgress, decimal? estimate = null)
        {
            var existing = Projects.FirstOrDefault(p => p.Name == project);
            if (existing is null)
            {
                existing = new Project { ProjectKey = Guid.NewGuid(), ProgrammeKey = Guid.NewGuid(), Name = project, CreatedAtUtc = Now, UpdatedAtUtc = Now };
                Projects.Add(existing);
                Workstreams.Add(new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = existing.ProjectKey, Name = "Issues", CreatedAtUtc = Now, UpdatedAtUtc = Now });
            }
            var item = new WorkItem
            {
                WorkItemKey = Guid.NewGuid(), WorkstreamKey = Workstreams.Single(w => w.ProjectKey == existing.ProjectKey).WorkstreamKey,
                Title = "Issue " + id, Stage = stage, EstimatedHours = estimate,
                ExternalSource = "Jira", ExternalId = $"{Cloud}:{id}", CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            Items.Add(item);
            return item;
        }

        /// <summary>A Tempo worklog: against a synced issue, an unsynced issue id, or no issue at all.</summary>
        public TimeEntry Worklog(decimal hours, WorkItem? issue = null, string? unsyncedIssueId = null, bool noPerson = false, string source = "Tempo")
        {
            var entry = new TimeEntry
            {
                TimeEntryKey = Guid.NewGuid(), WorkItemKey = issue?.WorkItemKey, StaffKey = noPerson ? null : Alex,
                DurationHours = hours, WorkDate = new DateOnly(2026, 9, 10), IsBillable = false, BillabilityKnown = false,
                ExternalSource = source, ExternalId = Guid.NewGuid().ToString(),
                SourceWorkItemExternalId = issue?.ExternalId ?? (unsyncedIssueId is null ? null : $"{Cloud}:{unsyncedIssueId}"),
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            Time.Add(entry);
            if (issue is not null)
                Recorded[issue.WorkItemKey] = Recorded.GetValueOrDefault(issue.WorkItemKey) + hours;
            return entry;
        }

        public JiraTempoReconciliationReport? Run() => JiraTempoReconciliationCalculator.Evaluate(new JiraTempoReconciliationInput(
            From, To,
            new SourceDataPresence(Items.Any(i => i.ExternalSource == "Jira"), false),
            new SourceDataPresence(false, Time.Any(t => t.ExternalSource == "Tempo")),
            JiraRun, TempoRun, Projects, Workstreams, Items, Time, Recorded));
    }

    // ---- Does the page exist, and in which shape

    [Fact]
    public void A_tenant_with_neither_source_has_no_report()
    {
        var d = new Data();
        d.Worklog(3, source: "ClickUp");

        Assert.Null(d.Run());
    }

    [Fact]
    public void Jira_only_shows_projects_but_no_hours_section_and_says_why()
    {
        var d = new Data();
        d.Issue("100");

        var report = d.Run()!;

        Assert.Equal(ReconciliationShape.JiraOnly, report.Shape);
        Assert.Null(report.Hours);
        Assert.Single(report.Projects);
        // Without a time source, "no time recorded" would blame the work for the setup.
        Assert.Null(report.Projects[0].OpenIssuesWithNoTimeRecorded);
        Assert.StartsWith("Tempo isn't connected", report.Notes[0]);
    }

    [Fact]
    public void Tempo_only_shows_every_hour_as_unlinked_with_its_issue()
    {
        var d = new Data();
        d.Worklog(5, unsyncedIssueId: "200");
        d.Worklog(2, unsyncedIssueId: "200");

        var report = d.Run()!;

        Assert.Equal(ReconciliationShape.TempoOnly, report.Shape);
        Assert.Empty(report.Projects);
        Assert.Equal(0, report.Hours!.Linked);
        Assert.Equal(7, report.Hours.IssueNotSynced);
        var issue = Assert.Single(report.UnlinkedIssues);
        Assert.Equal(("200", 2, 7m), (issue.IssueId, issue.Worklogs, issue.Hours));
        Assert.StartsWith("Jira isn't connected", report.Notes[0]);
    }

    [Fact]
    public void Tempo_only_says_when_jira_is_connected_for_identity_only()
    {
        var d = new Data { JiraRun = new SourceRunState("Jira Cloud", Now, LastRunFailed: false, IsRunning: false) };
        d.Worklog(5, unsyncedIssueId: "200");

        var report = d.Run()!;

        Assert.Equal(ReconciliationShape.TempoOnly, report.Shape);
        Assert.StartsWith("Jira is connected for identity only", report.Notes[0]);
    }

    // ---- Where the hours went

    [Fact]
    public void Linked_issue_not_synced_and_no_issue_always_add_up_to_the_total()
    {
        var d = new Data();
        var synced = d.Issue("100");
        d.Worklog(4, synced);
        d.Worklog(3, unsyncedIssueId: "999");
        d.Worklog(1.5m);

        var hours = d.Run()!.Hours!;

        Assert.Equal((8.5m, 4m, 3m, 1.5m), (hours.Total, hours.Linked, hours.IssueNotSynced, hours.NoIssue));
        Assert.Equal(hours.Total, hours.Linked + hours.IssueNotSynced + hours.NoIssue);
    }

    [Fact]
    public void Hours_from_other_time_sources_are_not_counted_as_tempo()
    {
        var d = new Data();
        var synced = d.Issue("100");
        d.Worklog(4, synced);
        d.Worklog(10, source: "FileImport");

        Assert.Equal(4, d.Run()!.Hours!.Total);
    }

    [Fact]
    public void Unmatched_people_are_reported_as_hours_and_never_named()
    {
        var d = new Data();
        d.Worklog(2, d.Issue("100"), noPerson: true);

        var report = d.Run()!;

        Assert.Equal(2, report.Hours!.WithoutPerson);
        Assert.Contains(report.Notes, n => n.Contains("not yet matched"));
    }

    [Fact]
    public void Unlinked_issues_are_largest_first_and_the_rest_are_summed_not_dropped()
    {
        var d = new Data();
        d.Issue("100");
        for (var i = 1; i <= JiraTempoReconciliationCalculator.UnlinkedIssuesShown + 2; i++)
            d.Worklog(i, unsyncedIssueId: $"X{i}");

        var report = d.Run()!;

        Assert.Equal(JiraTempoReconciliationCalculator.UnlinkedIssuesShown, report.UnlinkedIssues.Count);
        Assert.Equal($"X{JiraTempoReconciliationCalculator.UnlinkedIssuesShown + 2}", report.UnlinkedIssues[0].IssueId);
        Assert.Equal(2, report.MoreUnlinkedIssues);
        Assert.Equal(1m + 2m, report.MoreUnlinkedHours);
    }

    // ---- Per project

    [Fact]
    public void Projects_show_period_hours_open_issues_without_time_and_estimate_overruns()
    {
        var d = new Data();
        var over = d.Issue("1", estimate: 4);
        var within = d.Issue("2", estimate: 10);
        d.Issue("3");
        d.Issue("4", stage: WorkItemLifecycleStage.Done);
        d.Worklog(6, over);
        d.Worklog(2, within);

        var project = Assert.Single(d.Run()!.Projects);

        Assert.Equal((4, 3, 8m, 2), (project.Issues, project.OpenIssues, project.PeriodHours, project.IssuesWithPeriodTime));
        Assert.Equal(1, project.OpenIssuesWithNoTimeRecorded);
        Assert.Equal((1, 2), (project.OverEstimate, project.EstimatedIssuesWithTime));
    }

    [Fact]
    public void Estimate_overruns_use_all_recorded_time_not_just_the_period()
    {
        var d = new Data();
        var issue = d.Issue("1", estimate: 4);
        d.Worklog(1, issue);
        d.Recorded[issue.WorkItemKey] = 9; // eight more hours before the period

        Assert.Equal(1, Assert.Single(d.Run()!.Projects).OverEstimate);
    }

    // ---- Source health

    [Fact]
    public void A_failed_source_is_stated_first_and_never_read_as_zero()
    {
        var d = new Data();
        d.Worklog(3, d.Issue("100"));
        d.TempoRun = new SourceRunState("Tempo Timesheets", Now.AddDays(-1), LastRunFailed: true, IsRunning: false);

        var report = d.Run()!;

        Assert.StartsWith("Tempo Timesheets: the last sync failed", report.Notes[0]);
    }

    [Fact]
    public void Tempo_hours_are_never_presented_as_billable()
    {
        var d = new Data();
        d.Worklog(3, d.Issue("100"));

        Assert.Contains(d.Run()!.Notes, n => n.Contains("not billable hours"));
    }

    [Theory]
    [InlineData(Cloud + ":10042", "10042")]
    [InlineData("10042", "10042")]
    public void The_issue_id_is_the_part_after_the_site(string externalId, string expected) =>
        Assert.Equal(expected, JiraTempoReconciliationCalculator.IssueIdOf(externalId));
}
