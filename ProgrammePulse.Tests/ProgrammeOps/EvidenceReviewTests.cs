using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The weekly review loop: recording a check, carrying decisions forward,
/// comparing against last week, and the decision rules. Each rule is a
/// product claim ("next week's check shows whether decisions held"), so each
/// has a test that states it.
/// </summary>
public class EvidenceReviewTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly EvidenceScope WholeOrg = new(null, null, "All programmes / all customers", Today.AddDays(-6), Today);

    private static EvidenceFinding Finding(string key, decimal numerator, decimal denominator = 100,
        EvidenceFindingCategory category = EvidenceFindingCategory.EvidenceGap) =>
        new(key, category, EvidenceFindingSeverity.High, "Title " + key, "Why " + key, numerator, denominator, "items",
            [new EvidenceRecord("Alpha", "FileImport", key + "-1", "Item", "Detail")]);

    private static EvidenceCheckReport Report(EvidenceReadiness readiness, params EvidenceFinding[] findings) =>
        new(Now, readiness, "Reason", 100, 40m, findings, [], []);

    private static EvidenceReview Decided(EvidenceReview review, string key, FindingDisposition disposition, Guid? owner = null, DateOnly? target = null, string? note = null) =>
        review with
        {
            Findings = review.Findings.Select(f => f.FindingKey != key ? f : f with
            {
                Disposition = disposition, OwnerStaffKey = owner, TargetDate = target, Note = note,
                DecidedAtUtc = Now, DecidedByStaffKey = Owner, CarriedForward = false
            }).ToList()
        };

    // ---- Record

    [Fact]
    public void Recording_freezes_the_check_with_every_finding_and_its_source_records()
    {
        var review = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12), Finding("b", 3)), null, Tenant, Owner, Guid.NewGuid());

        Assert.Equal(EvidenceReadiness.NotDecisionReady, review.Readiness);
        Assert.Equal(["a", "b"], review.Findings.Select(f => f.FindingKey));
        Assert.All(review.Findings, f => Assert.Single(f.Records));
        Assert.All(review.Findings, f => Assert.Equal(FindingDisposition.Open, f.Disposition));
        Assert.All(review.Findings, f => Assert.False(f.CarriedForward));
    }

    [Fact]
    public void A_decision_carries_forward_to_the_next_review_and_says_it_was_carried()
    {
        var first = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12)), null, Tenant, Owner, Guid.NewGuid());
        first = Decided(first, "a", FindingDisposition.FixAtSource, Owner, Today.AddDays(7), "Map the statuses");

        var second = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 8)), first, Tenant, Owner, Guid.NewGuid());

        var carried = Assert.Single(second.Findings);
        Assert.Equal(FindingDisposition.FixAtSource, carried.Disposition);
        Assert.Equal(Owner, carried.OwnerStaffKey);
        Assert.Equal(Today.AddDays(7), carried.TargetDate);
        Assert.Equal("Map the statuses", carried.Note);
        Assert.True(carried.CarriedForward);
        Assert.Equal(8, carried.Numerator);
    }

    [Fact]
    public void A_finding_nobody_decided_is_not_marked_as_carried_forward()
    {
        var first = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Finding("a", 1)), null, Tenant, Owner, Guid.NewGuid());

        var second = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Finding("a", 1)), first, Tenant, Owner, Guid.NewGuid());

        Assert.False(Assert.Single(second.Findings).CarriedForward);
    }

    [Fact]
    public void A_finding_marked_resolved_that_is_still_found_goes_back_to_open()
    {
        var first = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12)), null, Tenant, Owner, Guid.NewGuid());
        first = Decided(first, "a", FindingDisposition.Resolved, Owner);

        var second = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12)), first, Tenant, Owner, Guid.NewGuid());

        var reopened = Assert.Single(second.Findings);
        Assert.Equal(FindingDisposition.Open, reopened.Disposition);
        Assert.Equal(Owner, reopened.OwnerStaffKey);
    }

    // ---- Compare

    [Fact]
    public void Movement_is_new_worse_unchanged_improved_or_cleared()
    {
        var before = EvidenceReviewCalculator.Record(
            Report(EvidenceReadiness.NotDecisionReady, Finding("worse", 2), Finding("same", 5), Finding("better", 9), Finding("gone", 4)),
            null, Tenant, Owner, Guid.NewGuid());
        var after = EvidenceReviewCalculator.Record(
            Report(EvidenceReadiness.UseWithCaveats, Finding("worse", 3), Finding("same", 5), Finding("better", 1), Finding("fresh", 7)),
            before, Tenant, Owner, Guid.NewGuid());

        var comparison = EvidenceReviewCalculator.Compare(after, before, Today);
        FindingTrend Trend(string key) => comparison.Findings.Single(f => f.FindingKey == key).Trend;

        Assert.Equal(FindingTrend.Worse, Trend("worse"));
        Assert.Equal(FindingTrend.Unchanged, Trend("same"));
        Assert.Equal(FindingTrend.Improved, Trend("better"));
        Assert.Equal(FindingTrend.Cleared, Trend("gone"));
        Assert.Equal(FindingTrend.New, Trend("fresh"));
        Assert.Equal(2, comparison.Improved);
        Assert.Equal(2, comparison.Worse);
        Assert.Equal(EvidenceReadiness.NotDecisionReady, comparison.PreviousReadiness);
        Assert.Equal(EvidenceReadiness.UseWithCaveats, comparison.CurrentReadiness);
    }

    [Fact]
    public void Movement_is_judged_on_the_count_so_more_clean_data_does_not_count_as_a_fix()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 10, 50)), null, Tenant, Owner, Guid.NewGuid());
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 10, 500)), before, Tenant, Owner, Guid.NewGuid());

        Assert.Equal(FindingTrend.Unchanged, Assert.Single(EvidenceReviewCalculator.Compare(after, before, Today).Findings).Trend);
    }

    [Fact]
    public void A_decision_that_did_not_hold_is_flagged()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("resolved", 3), Finding("late", 3), Finding("on-time", 3)),
            null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "resolved", FindingDisposition.Resolved, Owner);
        before = Decided(before, "late", FindingDisposition.FixAtSource, Owner, Today.AddDays(-1));
        before = Decided(before, "on-time", FindingDisposition.FixAtSource, Owner, Today.AddDays(3));
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("resolved", 3), Finding("late", 2), Finding("on-time", 3)),
            before, Tenant, Owner, Guid.NewGuid());

        var comparison = EvidenceReviewCalculator.Compare(after, before, Today);
        FindingComparison Row(string key) => comparison.Findings.Single(f => f.FindingKey == key);

        Assert.True(Row("resolved").ResolvedButStillPresent);
        Assert.True(Row("late").PastTargetDate);
        Assert.False(Row("on-time").PastTargetDate);
        Assert.Equal(2, comparison.BrokenCommitments);
    }

    [Fact]
    public void A_cleared_finding_is_never_a_broken_commitment()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)), null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "a", FindingDisposition.FixAtSource, Owner, Today.AddDays(-10));
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.DecisionReady), before, Tenant, Owner, Guid.NewGuid());

        var comparison = EvidenceReviewCalculator.Compare(after, before, Today);

        Assert.Equal(FindingTrend.Cleared, Assert.Single(comparison.Findings).Trend);
        Assert.Equal(0, comparison.BrokenCommitments);
    }

    // ---- Scope (GTM review 28 Sept, finding 1)

    [Fact]
    public void A_decision_never_carries_to_a_review_of_a_different_scope()
    {
        var portal = WholeOrg with { ProgrammeKey = Guid.NewGuid(), Label = "Portal / all customers" };
        var whole = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12)) with { Scope = WholeOrg },
            null, Tenant, Owner, Guid.NewGuid());
        whole = Decided(whole, "a", FindingDisposition.FixAtSource, Owner, Today.AddDays(7), "Map the statuses");

        var programme = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 12)) with { Scope = portal },
            whole, Tenant, Owner, Guid.NewGuid());

        var fresh = Assert.Single(programme.Findings);
        Assert.False(fresh.CarriedForward);
        Assert.Null(fresh.OwnerStaffKey);
        Assert.Equal(portal, programme.Scope);
    }

    [Fact]
    public async Task A_newer_review_of_one_programme_does_not_freeze_the_whole_organisation_review()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var whole = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;
        var portal = WholeOrg with { ProgrammeKey = Guid.NewGuid(), Label = "Portal / all customers" };
        var programme = (await harness.Service.RecordAsync(Tenant, portal, new EvidenceReviewActor(Owner, 7)))!;

        var decided = await harness.Service.DecideAsync(Tenant, whole.ReviewKey, "a", FindingDisposition.Resolved, null, null, null, new(Owner, 7));

        Assert.Equal(EvidenceDecisionStatus.Saved, decided.Status);
        // Each scope's live page compares with its own last review.
        Assert.Equal(programme.ReviewKey, (await harness.Service.BuildLiveAsync(Tenant, portal)).LastReview!.ReviewKey);
        Assert.Equal(whole.ReviewKey, (await harness.Service.BuildLiveAsync(Tenant, WholeOrg)).LastReview!.ReviewKey);
    }

    // ---- Record-level movement (GTM review 28 Sept, finding 3)

    private static EvidenceFinding Overdue(params string[] ids) =>
        new("overdue", EvidenceFindingCategory.DeliveryException, EvidenceFindingSeverity.High, "Open work past its due date", "Why",
            ids.Length, 100, "open items", ids.Select(id => new EvidenceRecord("Alpha", "FileImport", id, "Item " + id, "Due last week.")).ToList());

    private static string[] Ids(string prefix, int count) => Enumerable.Range(1, count).Select(i => $"{prefix}-{i}").ToArray();

    [Fact]
    public void Ten_fixed_and_ten_newly_late_items_are_not_reported_as_no_change()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue(Ids("A", 10))), null, Tenant, Owner, Guid.NewGuid());
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue(Ids("B", 10))), before, Tenant, Owner, Guid.NewGuid());

        var comparison = EvidenceReviewCalculator.Compare(after, before, Today);
        var row = Assert.Single(comparison.Findings);

        Assert.Equal(FindingTrend.Shifted, row.Trend);
        Assert.Equal(new RecordMovement(StillPresent: 0, Appeared: 10, Cleared: 10), row.Records);
        Assert.Equal(1, comparison.Shifted);
    }

    [Fact]
    public void Movement_counts_the_records_that_stayed_appeared_and_cleared()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-1", "A-2", "A-3")), null, Tenant, Owner, Guid.NewGuid());
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-2", "A-3", "B-1", "B-2")), before, Tenant, Owner, Guid.NewGuid());

        var row = Assert.Single(EvidenceReviewCalculator.Compare(after, before, Today).Findings);

        Assert.Equal(FindingTrend.Worse, row.Trend);
        Assert.Equal(new RecordMovement(StillPresent: 2, Appeared: 2, Cleared: 1), row.Records);
    }

    [Fact]
    public void A_decision_is_not_carried_to_records_it_never_covered()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue(Ids("A", 10))), null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "overdue", FindingDisposition.FixAtSource, Owner, Today.AddDays(-1), "Replan");

        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue(Ids("B", 10))), before, Tenant, Owner, Guid.NewGuid());
        var fresh = Assert.Single(after.Findings);
        Assert.False(fresh.CarriedForward);
        Assert.Null(fresh.OwnerStaffKey);
        Assert.Equal(FindingDisposition.Open, fresh.Disposition);

        var row = Assert.Single(EvidenceReviewCalculator.Compare(after, before, Today).Findings);
        Assert.True(row.DecisionNotCarried);
        // Every record the decision covered cleared, so it held; the late ones are new.
        Assert.False(row.PastTargetDate);
    }

    [Fact]
    public void A_decision_carries_forward_while_any_record_it_covered_remains()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-1", "A-2")), null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "overdue", FindingDisposition.FixAtSource, Owner, Today.AddDays(-1));

        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-2", "B-1")), before, Tenant, Owner, Guid.NewGuid());

        Assert.True(Assert.Single(after.Findings).CarriedForward);
        var row = Assert.Single(EvidenceReviewCalculator.Compare(after, before, Today).Findings);
        Assert.Equal(FindingTrend.Shifted, row.Trend);
        Assert.True(row.PastTargetDate);
        Assert.False(row.DecisionNotCarried);
    }

    [Fact]
    public void Resolved_is_contradicted_only_by_the_records_it_resolved()
    {
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-1")), null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "overdue", FindingDisposition.Resolved, Owner);

        var replaced = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("B-1")), before, Tenant, Owner, Guid.NewGuid());
        Assert.False(Assert.Single(EvidenceReviewCalculator.Compare(replaced, before, Today).Findings).ResolvedButStillPresent);

        var stillThere = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, Overdue("A-1")), before, Tenant, Owner, Guid.NewGuid());
        Assert.True(Assert.Single(EvidenceReviewCalculator.Compare(stillThere, before, Today).Findings).ResolvedButStillPresent);
    }

    [Fact]
    public void A_finding_with_no_records_is_compared_on_its_count_alone()
    {
        EvidenceFinding People(decimal count) =>
            new("unmatched-people", EvidenceFindingCategory.EvidenceGap, EvidenceFindingSeverity.Medium, "People", "Why", count, count, "people awaiting a match", []);
        var before = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, People(4)), null, Tenant, Owner, Guid.NewGuid());
        before = Decided(before, "unmatched-people", FindingDisposition.FixAtSource, Owner, Today.AddDays(3));
        var after = EvidenceReviewCalculator.Record(Report(EvidenceReadiness.UseWithCaveats, People(4)), before, Tenant, Owner, Guid.NewGuid());

        var row = Assert.Single(EvidenceReviewCalculator.Compare(after, before, Today).Findings);
        Assert.Equal(FindingTrend.Unchanged, row.Trend);
        Assert.Null(row.Records);
        Assert.True(Assert.Single(after.Findings).CarriedForward);
    }

    [Fact]
    public void A_record_without_an_external_id_is_matched_on_project_and_title()
    {
        var a = new EvidenceRecord("Alpha", null, null, "Migrate billing", "Due 2026-09-01, 3 days late.");
        var b = a with { Detail = "Due 2026-09-01, 10 days late." };

        Assert.Equal(EvidenceRecordIdentity.Of(a), EvidenceRecordIdentity.Of(b));
        Assert.NotEqual(EvidenceRecordIdentity.Of(a), EvidenceRecordIdentity.Of(a with { Project = "Beta" }));
    }

    // ---- Validate

    [Theory]
    [InlineData(FindingDisposition.FixAtSource, false, true, null, "owner")]
    [InlineData(FindingDisposition.FixAtSource, true, false, null, "target date")]
    [InlineData(FindingDisposition.AcceptAndState, false, false, " ", "caveat")]
    [InlineData(FindingDisposition.Disputed, false, false, null, "reason")]
    public void A_decision_without_what_makes_it_actionable_is_refused(FindingDisposition disposition, bool owner, bool target, string? note, string expected)
    {
        var error = EvidenceReviewCalculator.Validate(disposition, owner ? Owner : null, target ? Today : null, note);

        Assert.NotNull(error);
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FindingDisposition.Open)]
    [InlineData(FindingDisposition.Resolved)]
    public void Open_and_resolved_need_nothing_extra(FindingDisposition disposition) =>
        Assert.Null(EvidenceReviewCalculator.Validate(disposition, null, null, null));

    [Fact]
    public void An_over_long_note_or_unknown_decision_is_refused()
    {
        Assert.NotNull(EvidenceReviewCalculator.Validate(FindingDisposition.Open, null, null, new string('x', EvidenceReviewCalculator.MaxNoteLength + 1)));
        Assert.NotNull(EvidenceReviewCalculator.Validate((FindingDisposition)99, null, null, null));
    }

    // ---- Service

    [Fact]
    public async Task Nothing_is_recorded_when_there_is_no_data()
    {
        var harness = new Harness(Report(EvidenceReadiness.NoData));

        Assert.Null(await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)));
        Assert.Empty(harness.Reviews.Stored);
        Assert.Empty(harness.Audit.Actions);
    }

    [Fact]
    public async Task Recording_stores_the_review_and_audits_it()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));

        var review = await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7));

        Assert.NotNull(review);
        Assert.Equal(review.ReviewKey, Assert.Single(harness.Reviews.Stored).ReviewKey);
        Assert.Equal(["Recorded"], harness.Audit.Actions);
    }

    [Fact]
    public async Task The_live_page_compares_against_the_last_recorded_review()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7));
        harness.Check.Report = Report(EvidenceReadiness.UseWithCaveats, Finding("a", 1));

        var page = await harness.Service.BuildLiveAsync(Tenant, WholeOrg);

        Assert.NotNull(page.SinceLastReview);
        Assert.Equal(FindingTrend.Improved, Assert.Single(page.SinceLastReview.Findings).Trend);
    }

    [Fact]
    public async Task An_owner_must_be_active_staff_in_the_same_tenant()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var review = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;
        var foreign = harness.AddStaff(Guid.NewGuid(), active: true);
        var inactive = harness.AddStaff(Tenant, active: false);

        foreach (var candidate in new[] { foreign, inactive, Guid.NewGuid() })
        {
            var result = await harness.Service.DecideAsync(Tenant, review.ReviewKey, "a", FindingDisposition.FixAtSource, candidate, Today.AddDays(5), null, new(Owner, 7));
            Assert.Equal(EvidenceDecisionStatus.Invalid, result.Status);
        }

        Assert.Null(harness.Reviews.Stored.Single().Findings.Single().DecidedAtUtc);
    }

    [Fact]
    public async Task A_valid_decision_is_saved_and_audited()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var review = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;
        var owner = harness.AddStaff(Tenant, active: true);

        var result = await harness.Service.DecideAsync(Tenant, review.ReviewKey, "a", FindingDisposition.FixAtSource, owner, Today.AddDays(5), "Map it", new(Owner, 7));

        Assert.Equal(EvidenceDecisionStatus.Saved, result.Status);
        var saved = harness.Reviews.Stored.Single().Findings.Single();
        Assert.Equal(owner, saved.OwnerStaffKey);
        Assert.Equal(Owner, saved.DecidedByStaffKey);
        Assert.Equal(["Recorded", "Decided"], harness.Audit.Actions);
    }

    [Fact]
    public async Task A_target_date_in_the_past_is_refused()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var review = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;
        var owner = harness.AddStaff(Tenant, active: true);

        var result = await harness.Service.DecideAsync(Tenant, review.ReviewKey, "a", FindingDisposition.FixAtSource, owner, Today.AddDays(-1), null, new(Owner, 7));

        Assert.Equal(EvidenceDecisionStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task An_earlier_review_is_history_and_takes_no_decisions()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var first = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;
        harness.Check.Report = harness.Check.Report with { AsOfUtc = Now.AddDays(7) };
        await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7));

        var result = await harness.Service.DecideAsync(Tenant, first.ReviewKey, "a", FindingDisposition.Resolved, null, null, null, new(Owner, 7));

        Assert.Equal(EvidenceDecisionStatus.NotLatest, result.Status);
    }

    [Fact]
    public async Task Another_tenants_review_is_not_found()
    {
        var harness = new Harness(Report(EvidenceReadiness.NotDecisionReady, Finding("a", 3)));
        var review = (await harness.Service.RecordAsync(Tenant, WholeOrg, new EvidenceReviewActor(Owner, 7)))!;

        Assert.Null(await harness.Service.GetReviewAsync(Guid.NewGuid(), review.ReviewKey));
        Assert.Equal(EvidenceDecisionStatus.NotFound,
            (await harness.Service.DecideAsync(Guid.NewGuid(), review.ReviewKey, "a", FindingDisposition.Resolved, null, null, null, new(Owner, 7))).Status);
    }

    private sealed class Harness
    {
        public Harness(EvidenceCheckReport report)
        {
            Check = new FakeCheck { Report = report };
            Service = new EvidenceReviewService(Check, Reviews, Staff, Audit, Clock);
        }

        public FakeCheck Check { get; }
        public FakeReviewRepository Reviews { get; } = new();
        public FakeStaffRepository Staff { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FixedClock Clock { get; } = new(Now);
        public EvidenceReviewService Service { get; }

        public Guid AddStaff(Guid tenant, bool active)
        {
            var key = Guid.NewGuid();
            Staff.Staff.Add(new StaffProfile
            {
                StaffKey = key, MemberId = Staff.Staff.Count + 100, FullName = "Person " + key.ToString("N")[..6], Email = key + "@example.test",
                TenantId = tenant, IsActive = active, CreatedAtUtc = Now, UpdatedAtUtc = Now
            });
            return key;
        }
    }

    private sealed class FakeCheck : IEvidenceCheckService
    {
        public required EvidenceCheckReport Report { get; set; }

        public Task<EvidenceScopeResolution> ResolveScopeAsync(Guid tenantId, EvidenceScopeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvidenceScopeResolution(WholeOrg, null, Models.ViewModels.ProgrammeOverview.PortfolioScopeViewModel.All));

        public Task<EvidenceCheckReport> BuildAsync(Guid tenantId, EvidenceScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(Report with { Scope = scope });
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeAudit : IAuditLogRepository
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => throw new NotSupportedException();

        public Task<AuditLog?> GetLatestAsync(string entityType, string action) => throw new NotSupportedException();
    }

    /// <summary>In-memory, with the same latest-only rule the SQL repository enforces.</summary>
    private sealed class FakeReviewRepository : IEvidenceReviewRepository
    {
        public List<EvidenceReview> Stored { get; } = [];

        private IEnumerable<EvidenceReview> For(Guid tenantId) =>
            Stored.Where(r => r.TenantId == tenantId).OrderByDescending(r => r.RecordedAtUtc);

        public Task AddAsync(EvidenceReview review)
        {
            Stored.Add(review);
            return Task.CompletedTask;
        }

        public Task<EvidenceReview?> GetAsync(Guid tenantId, Guid reviewKey) =>
            Task.FromResult(For(tenantId).FirstOrDefault(r => r.ReviewKey == reviewKey));

        public Task<EvidenceReview?> GetLatestAsync(Guid tenantId, string scopeKey) =>
            Task.FromResult(For(tenantId).FirstOrDefault(r => r.ScopeKey == scopeKey));

        public Task<EvidenceReview?> GetPreviousAsync(Guid tenantId, Guid reviewKey)
        {
            var current = For(tenantId).FirstOrDefault(r => r.ReviewKey == reviewKey);
            return Task.FromResult(current is null ? null
                : For(tenantId).Where(r => r.ScopeKey == current.ScopeKey).SkipWhile(r => r.ReviewKey != reviewKey).Skip(1).FirstOrDefault());
        }

        public Task<IReadOnlyList<EvidenceReview>> GetHistoryAsync(Guid tenantId, int take) =>
            Task.FromResult<IReadOnlyList<EvidenceReview>>(For(tenantId).Take(take).ToList());

        public Task<DecisionWriteOutcome> DecideAsync(Guid tenantId, Guid reviewKey, string findingKey, FindingDisposition disposition,
            Guid? ownerStaffKey, DateOnly? targetDate, string? note, Guid? decidedByStaffKey, DateTime decidedAtUtc)
        {
            var target = For(tenantId).FirstOrDefault(r => r.ReviewKey == reviewKey);
            if (target is null)
                return Task.FromResult(DecisionWriteOutcome.NotFound);
            var latest = For(tenantId).First(r => r.ScopeKey == target.ScopeKey);
            if (latest.ReviewKey != reviewKey)
                return Task.FromResult(For(tenantId).Any(r => r.ReviewKey == reviewKey) ? DecisionWriteOutcome.NotLatest : DecisionWriteOutcome.NotFound);
            if (latest.Findings.All(f => f.FindingKey != findingKey))
                return Task.FromResult(DecisionWriteOutcome.NotFound);

            Stored[Stored.IndexOf(latest)] = latest with
            {
                Findings = latest.Findings.Select(f => f.FindingKey != findingKey ? f : f with
                {
                    Disposition = disposition, OwnerStaffKey = ownerStaffKey, TargetDate = targetDate, Note = note,
                    DecidedAtUtc = decidedAtUtc, DecidedByStaffKey = decidedByStaffKey, CarriedForward = false
                }).ToList()
            };
            return Task.FromResult(DecisionWriteOutcome.Saved);
        }

        public Task<IReadOnlyList<EvidenceReviewStaffReference>> GetStaffReferencesAsync(Guid staffKey) => throw new NotSupportedException();

        public Task EraseStaffReferencesAsync(Guid staffKey) => throw new NotSupportedException();
    }
}
