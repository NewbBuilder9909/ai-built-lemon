using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Every Evidence Check rule, against hand-built Silver data. The rules are
/// the product claim, so each one has a test that states it.
/// </summary>
public class EvidenceCheckCalculatorTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Alex = Guid.NewGuid();

    private sealed class Data
    {
        public List<Project> Projects { get; } = [];
        public List<Workstream> Workstreams { get; } = [];
        public List<WorkItem> Items { get; } = [];
        public List<TimeEntry> Time { get; } = [];
        public List<SourceRunState> Sources { get; } = [];
        public int Unresolved { get; set; }
        public EvidenceScope? Scope { get; set; }
        public Dictionary<Guid, decimal>? Recorded { get; set; }
        public decimal OutsideScope { get; set; }

        public Guid Stream(string project)
        {
            var existing = Projects.FirstOrDefault(p => p.Name == project);
            if (existing is null)
            {
                existing = new Project { ProjectKey = Guid.NewGuid(), ProgrammeKey = Guid.NewGuid(), Name = project, CreatedAtUtc = Now, UpdatedAtUtc = Now };
                Projects.Add(existing);
                Workstreams.Add(new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = existing.ProjectKey, Name = "Work", CreatedAtUtc = Now, UpdatedAtUtc = Now });
            }
            return Workstreams.Single(w => w.ProjectKey == existing.ProjectKey).WorkstreamKey;
        }

        /// <summary>A healthy item by default: owned, estimated, dated in the future.</summary>
        public WorkItem Item(string id, WorkItemLifecycleStage stage, string project = "Alpha", Guid? owner = null, bool unowned = false,
            decimal? estimate = 8, bool noEstimate = false, DateTime? due = null, bool noDue = false, string source = "FileImport")
        {
            var item = new WorkItem
            {
                WorkItemKey = Guid.NewGuid(), WorkstreamKey = Stream(project), Title = "Item " + id, Stage = stage,
                RawStatus = stage.ToString(), AssignedStaffKey = unowned ? null : owner ?? Alex,
                EstimatedHours = noEstimate ? null : estimate, DueDateUtc = noDue ? null : due ?? Now.AddDays(14),
                ExternalSource = source, ExternalId = id, CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            Items.Add(item);
            return item;
        }

        public TimeEntry Log(WorkItem? item, decimal hours, bool noPerson = false, bool? billable = true)
        {
            var entry = new TimeEntry
            {
                TimeEntryKey = Guid.NewGuid(), WorkItemKey = item?.WorkItemKey, StaffKey = noPerson ? null : Alex,
                DurationHours = hours, WorkDate = DateOnly.FromDateTime(Now), IsBillable = billable ?? false,
                BillabilityKnown = billable is not null, ExternalSource = "FileImport", ExternalId = Guid.NewGuid().ToString(),
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            };
            Time.Add(entry);
            return entry;
        }

        public EvidenceCheckReport Run() =>
            EvidenceCheckCalculator.Evaluate(new EvidenceCheckInput(Projects, Workstreams, Items, Time, Unresolved, Sources, Scope, Recorded, OutsideScope), Now);
    }

    private static EvidenceFinding? Find(EvidenceCheckReport report, string key) => report.Findings.SingleOrDefault(f => f.Key == key);

    [Fact]
    public void No_work_items_is_no_data_not_a_clean_bill_of_health()
    {
        var report = new Data().Run();

        Assert.Equal(EvidenceReadiness.NoData, report.Readiness);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Complete_evidence_is_decision_ready_even_with_bad_news_in_it()
    {
        var d = new Data();
        var late = d.Item("A-1", WorkItemLifecycleStage.InProgress, due: Now.AddDays(-3));
        d.Item("A-2", WorkItemLifecycleStage.Blocked);
        d.Log(late, 4);

        var report = d.Run();

        Assert.Equal(EvidenceReadiness.DecisionReady, report.Readiness);
        Assert.DoesNotContain(report.Findings, f => f.Category == EvidenceFindingCategory.EvidenceGap);
        var overdue = Find(report, "overdue")!;
        Assert.Equal(1, overdue.Numerator);
        Assert.Equal(2, overdue.Denominator);
        Assert.Contains("3 days late", Assert.Single(overdue.Records).Detail);
        Assert.NotNull(Find(report, "blocked"));
    }

    [Fact]
    public void Material_unmapped_status_makes_the_report_not_decision_ready_and_says_why()
    {
        var d = new Data();
        for (var i = 0; i < 9; i++) d.Item($"A-{i}", WorkItemLifecycleStage.InProgress);
        d.Item("A-9", WorkItemLifecycleStage.Unmapped);
        d.Log(d.Items[0], 1);

        var report = d.Run();

        Assert.Equal(EvidenceReadiness.NotDecisionReady, report.Readiness);
        Assert.Contains("status can't be read", report.ReadinessReason);
        var unmapped = Find(report, "unmapped-status")!;
        Assert.Equal((1m, 10m), (unmapped.Numerator, unmapped.Denominator));
        Assert.Contains("\"Unmapped\"", unmapped.Records.Single().Detail);
    }

    [Fact]
    public void A_high_severity_gap_below_the_material_share_only_needs_caveats()
    {
        var d = new Data();
        for (var i = 0; i < 20; i++) d.Item($"A-{i}", WorkItemLifecycleStage.InProgress);
        d.Item("A-20", WorkItemLifecycleStage.Unmapped);
        d.Log(d.Items[0], 1);

        Assert.Equal(EvidenceReadiness.UseWithCaveats, d.Run().Readiness);
    }

    [Fact]
    public void Ownership_estimate_and_due_date_gaps_count_committed_work_only()
    {
        var d = new Data();
        d.Item("B-1", WorkItemLifecycleStage.Backlog, unowned: true, noEstimate: true, noDue: true);
        d.Item("A-1", WorkItemLifecycleStage.InProgress, unowned: true, noEstimate: true, noDue: true);
        d.Item("A-2", WorkItemLifecycleStage.Ready);
        d.Item("A-3", WorkItemLifecycleStage.Done, unowned: true);
        d.Log(d.Items[3], 1);

        var report = d.Run();

        foreach (var key in new[] { "committed-without-owner", "committed-without-estimate", "committed-without-due-date" })
        {
            var finding = Find(report, key)!;
            Assert.Equal((1m, 2m), (finding.Numerator, finding.Denominator));
            Assert.Equal("A-1", finding.Records.Single().ExternalId);
        }
    }

    [Fact]
    public void Time_gaps_are_measured_in_hours_not_rows()
    {
        var d = new Data();
        var item = d.Item("A-1", WorkItemLifecycleStage.InProgress);
        d.Log(item, 6);
        d.Log(null, 3);
        d.Log(item, 1, noPerson: true, billable: null);

        var report = d.Run();

        Assert.Equal(10m, report.RecordedHours);
        Assert.Equal((3m, 10m), (Find(report, "time-not-linked")!.Numerator, Find(report, "time-not-linked")!.Denominator));
        Assert.Equal(1m, Find(report, "time-without-person")!.Numerator);
        Assert.Equal(1m, Find(report, "billability-unknown")!.Numerator);
        Assert.Equal("(not linked to a project)", Find(report, "time-not-linked")!.Records.Single().Project);
    }

    [Fact]
    public void Finished_work_without_time_is_only_flagged_for_sources_that_record_time()
    {
        var d = new Data();
        var timed = d.Item("F-1", WorkItemLifecycleStage.InProgress, source: "FileImport");
        d.Item("F-2", WorkItemLifecycleStage.Done, source: "FileImport");
        d.Item("J-1", WorkItemLifecycleStage.Done, source: "Jira");
        d.Log(timed, 2);

        var finding = Find(d.Run(), "done-without-time")!;

        Assert.Equal((1m, 1m), (finding.Numerator, finding.Denominator));
        Assert.Equal("F-2", finding.Records.Single().ExternalId);
    }

    [Fact]
    public void No_time_at_all_is_a_stated_caveat_not_a_verdict()
    {
        var d = new Data();
        d.Item("A-1", WorkItemLifecycleStage.InProgress);

        var report = d.Run();

        Assert.Equal(EvidenceReadiness.UseWithCaveats, report.Readiness);
        Assert.Equal(EvidenceFindingSeverity.Medium, Find(report, "no-time-evidence")!.Severity);
    }

    [Fact]
    public void Overspend_and_uncommitted_effort_are_delivery_exceptions()
    {
        var d = new Data();
        var over = d.Item("A-1", WorkItemLifecycleStage.InProgress, estimate: 10);
        var backlog = d.Item("A-2", WorkItemLifecycleStage.Backlog);
        d.Log(over, 15);
        d.Log(backlog, 5);

        var report = d.Run();

        var overEstimate = Find(report, "over-estimate")!;
        Assert.Equal(EvidenceFindingCategory.DeliveryException, overEstimate.Category);
        Assert.Contains("15 h against 10 h estimate (150", overEstimate.Records.Single().Detail);
        Assert.Equal((5m, 20m), (Find(report, "time-on-uncommitted-work")!.Numerator, Find(report, "time-on-uncommitted-work")!.Denominator));
        Assert.Equal(EvidenceReadiness.DecisionReady, report.Readiness);
    }

    [Fact]
    public void Overdue_ignores_finished_unmapped_and_due_today()
    {
        var d = new Data();
        d.Item("A-1", WorkItemLifecycleStage.Done, due: Now.AddDays(-5));
        d.Item("A-2", WorkItemLifecycleStage.Unmapped, due: Now.AddDays(-5));
        d.Item("A-3", WorkItemLifecycleStage.InProgress, due: Now.Date);
        d.Item("A-4", WorkItemLifecycleStage.Ready, due: Now.AddDays(-1));

        Assert.Equal("A-4", Find(d.Run(), "overdue")!.Records.Single().ExternalId);
    }

    [Fact]
    public void A_failed_source_blocks_readiness_and_a_stale_one_caps_it_at_use_with_caveats()
    {
        var d = new Data();
        d.Log(d.Item("A-1", WorkItemLifecycleStage.InProgress), 1);
        d.Sources.Add(new SourceRunState("Jira Cloud", Now.AddDays(-2), LastRunFailed: false, IsRunning: false));
        Assert.Equal(EvidenceReadiness.DecisionReady, d.Run().Readiness);

        // Clean but ten days old: a weekly review can't call that decision-ready.
        d.Sources[0] = new SourceRunState("Jira Cloud", Now.AddDays(-10), LastRunFailed: false, IsRunning: false);
        var stale = d.Run();
        Assert.Equal(EvidenceReadiness.UseWithCaveats, stale.Readiness);
        Assert.Contains("Jira Cloud last published more than 7 days ago", stale.ReadinessReason);
        Assert.Contains(stale.SourceNotes, n => n.Contains("more than 7 days ago"));

        d.Sources.Add(new SourceRunState("ClickUp", Now.AddDays(-1), LastRunFailed: true, IsRunning: false));
        var failed = d.Run();
        Assert.Equal(EvidenceReadiness.NotDecisionReady, failed.Readiness);
        Assert.Contains(failed.SourceNotes, n => n.StartsWith("ClickUp: the last sync failed"));
    }

    [Fact]
    public void Projects_get_their_own_readiness_from_their_own_records()
    {
        var d = new Data();
        d.Item("C-1", WorkItemLifecycleStage.InProgress, project: "Clean");
        d.Item("M-1", WorkItemLifecycleStage.Unmapped, project: "Messy");
        d.Item("M-2", WorkItemLifecycleStage.InProgress, project: "Messy");
        d.Log(d.Items[0], 1);

        var projects = d.Run().Projects.ToDictionary(p => p.Project);

        Assert.Equal(EvidenceReadiness.DecisionReady, projects["Clean"].Readiness);
        Assert.Equal(EvidenceReadiness.NotDecisionReady, projects["Messy"].Readiness);
        Assert.Equal("Messy", d.Run().Projects[0].Project);
    }

    [Fact]
    public void Unmatched_people_are_reported_without_naming_them()
    {
        var d = new Data { Unresolved = 3 };
        d.Log(d.Item("A-1", WorkItemLifecycleStage.InProgress), 1);

        var finding = Find(d.Run(), "unmatched-people")!;

        Assert.Equal(3m, finding.Numerator);
        Assert.Empty(finding.Records);
    }

    /// <summary>
    /// The page is open to delivery-reporting roles below Admin, so cost
    /// isolation must be structural (CLAUDE.md, "Cost/rate data isolation is
    /// structural"): the service can't be handed anything that reads rates.
    /// </summary>
    [Fact]
    public void The_evidence_check_cannot_reach_cost_rates()
    {
        var dependencies = typeof(EvidenceCheckService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IStaffRateRepository), dependencies);
        Assert.DoesNotContain(dependencies, t => t.Name.Contains("Rate", StringComparison.Ordinal) || t.Name.Contains("Cost", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(EvidenceCheckReport).GetProperties(), p => p.Name.Contains("Cost", StringComparison.Ordinal));
    }

    // ---- Declared scope (GTM review 28 Sept, finding 1)

    private static EvidenceScope Week(DateOnly? extractedOn = null) =>
        new(null, null, "All programmes / all customers", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 26), extractedOn);

    [Fact]
    public void Clean_data_extracted_more_than_a_week_ago_is_not_decision_ready_whenever_it_was_uploaded()
    {
        var d = new Data();
        d.Log(d.Item("A-1", WorkItemLifecycleStage.InProgress), 1);

        d.Scope = Week(new DateOnly(2026, 9, 26));
        Assert.Equal(EvidenceReadiness.DecisionReady, d.Run().Readiness);

        d.Scope = Week(new DateOnly(2026, 9, 15));
        var old = d.Run();
        Assert.Equal(EvidenceReadiness.UseWithCaveats, old.Readiness);
        Assert.Contains("extracted on 2026-09-15, more than 7 days ago", old.ReadinessReason);
    }

    [Fact]
    public void An_extract_taken_before_the_period_ends_caps_readiness_and_says_its_last_days_are_missing()
    {
        var d = new Data();
        d.Log(d.Item("A-1", WorkItemLifecycleStage.InProgress), 1);
        d.Scope = Week(new DateOnly(2026, 9, 24));

        var report = d.Run();

        Assert.Equal(EvidenceReadiness.UseWithCaveats, report.Readiness);
        Assert.Contains("before the period ends on 2026-09-26", report.ReadinessReason);
    }

    [Fact]
    public void Estimate_overruns_use_all_recorded_time_not_just_the_period()
    {
        var d = new Data();
        var item = d.Item("A-1", WorkItemLifecycleStage.InProgress, estimate: 4);
        d.Log(item, 1);
        d.Scope = Week();
        d.Recorded = new() { [item.WorkItemKey] = 9 };

        var finding = Find(d.Run(), "over-estimate")!;

        Assert.Equal(1, finding.Numerator);
        Assert.StartsWith("9 h against 4 h", finding.Records.Single().Detail);
        Assert.Contains("not just this period", finding.WhyItMatters);
    }

    [Fact]
    public void Finished_work_whose_time_fell_before_the_period_is_not_called_free()
    {
        var d = new Data();
        var done = d.Item("A-1", WorkItemLifecycleStage.Done);
        var busy = d.Item("A-2", WorkItemLifecycleStage.InProgress);
        d.Log(busy, 2);
        d.Scope = Week();
        Assert.NotNull(Find(d.Run(), "done-without-time")); // no lifetime figure: looks free

        d.Recorded = new() { [done.WorkItemKey] = 5, [busy.WorkItemKey] = 2 };
        Assert.Null(Find(d.Run(), "done-without-time"));
    }

    [Fact]
    public void Time_that_no_work_item_places_in_the_scope_is_stated_not_dropped()
    {
        var d = new Data();
        d.Log(d.Item("A-1", WorkItemLifecycleStage.InProgress), 1);
        d.Scope = Week() with { ProgrammeKey = Guid.NewGuid(), Label = "Portal / all customers" };
        d.OutsideScope = 6;

        Assert.Contains(d.Run().SourceNotes, n => n.StartsWith("6 hours recorded in the period aren't linked to any work item"));
    }

    [Fact]
    public void A_period_with_no_time_says_so_rather_than_claiming_none_was_ever_recorded()
    {
        var d = new Data();
        d.Item("A-1", WorkItemLifecycleStage.InProgress);
        d.Scope = Week();

        Assert.Equal("No recorded time in this period", Find(d.Run(), "no-time-evidence")!.Title);
    }

    [Fact]
    public void A_scope_key_ignores_the_period_so_each_week_compares_with_the_last()
    {
        var programme = Guid.NewGuid();
        var thisWeek = Week() with { ProgrammeKey = programme };
        var lastWeek = thisWeek with { PeriodFrom = new DateOnly(2026, 9, 13), PeriodTo = new DateOnly(2026, 9, 19) };

        Assert.Equal(thisWeek.Key, lastWeek.Key);
        Assert.NotEqual(thisWeek.Key, Week().Key);
        Assert.Equal(EvidenceScope.WholeOrganisationKey, Week().Key);
    }
    // Record detail reaches the board pack's appendix, so it reads as English:
    // no enum names ("InProgress") and no "1 days" (design and PMO data review, D12).
    [Fact]
    public void Record_detail_names_stages_in_words_and_counts_one_day_in_the_singular()
    {
        var d = new Data();
        d.Item("A-1", WorkItemLifecycleStage.InProgress, due: Now.AddDays(-1));
        d.Item("A-2", WorkItemLifecycleStage.InReview, unowned: true);

        var report = d.Run();

        var late = Assert.Single(Find(report, "overdue")!.Records).Detail;
        Assert.Contains("1 day late", late);
        Assert.Contains("stage: in progress", late);
        Assert.Equal("Stage: In review.", Assert.Single(Find(report, "committed-without-owner")!.Records).Detail);
        Assert.DoesNotContain(report.Findings.SelectMany(f => f.Records), r => r.Detail.Contains("InProgress") || r.Detail.Contains("InReview"));
    }
}
