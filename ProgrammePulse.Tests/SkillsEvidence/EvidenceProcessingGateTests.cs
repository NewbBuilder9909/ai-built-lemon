using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The Slice 4 gate: person-level evidence collection refuses to run
/// without a current data-processing decision.
///
/// The design document requires the customer's worker-notice,
/// lawful-basis and DPIA decision to be recorded *before* person-level
/// evidence is collected. These tests are what make that a property of
/// the software rather than a sentence in a document — the sentence is
/// what the next person skips.
///
/// The gate deliberately does **not** cover the skills matrix. That is
/// self-declared data the subject can see and challenge, which is the
/// mild end of worker monitoring; repository evidence is collected about
/// people from a third-party system, which is not. Drawing the line
/// somewhere keeps the gate meaningful instead of something customers
/// learn to click through.
/// </summary>
public class EvidenceProcessingGateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Withdrawal_or_expiry_during_fetch_prevents_payload_evidence_and_coverage_writes(bool expire)
    {
        var ctx = WithCommits();
        ctx.Client.DuringCommitFetch = () =>
        {
            if (expire) ctx.Time.Advance(TimeSpan.FromDays(400));
            else ctx.BlockCollection(EvidenceTestContext.TenantA);
        };
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));
        Assert.Empty(ctx.Repository.Raw);
        Assert.Empty(ctx.Repository.Evidence);
        Assert.Empty(await ctx.Repository.GetCoverageForConnectionAsync(ctx.ConnectionA.ConnectionKey, EvidenceTestContext.TenantA));
        Assert.Single(ctx.Client.ObservedCommitCursors);
    }

    private static EvidenceTestContext WithCommits()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));
        return ctx;
    }

    [Fact]
    public async Task With_no_decision_recorded_collection_is_refused_and_nothing_is_fetched()
    {
        var ctx = WithCommits();
        ctx.BlockCollection(EvidenceTestContext.TenantA);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("lawful basis", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The gate is checked before anything is fetched, so no request
        // was made and no Bronze capture written.
        Assert.Empty(ctx.Repository.Evidence);
        Assert.Empty(ctx.Repository.Raw);
    }

    [Fact]
    public async Task Without_a_worker_notice_collection_is_refused()
    {
        var ctx = WithCommits();
        ctx.BlockCollection(EvidenceTestContext.TenantA);
        ctx.Continuity.Decisions.Add(Decision(ctx, workerNotice: false));

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("not been told", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_a_dpia_collection_is_refused()
    {
        var ctx = WithCommits();
        ctx.BlockCollection(EvidenceTestContext.TenantA);
        ctx.Continuity.Decisions.Add(Decision(ctx, dpia: false));

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("DPIA", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_expired_decision_stops_collection_until_it_is_renewed()
    {
        var ctx = WithCommits();
        await ctx.RunAsync(ctx.ConnectionA);
        Assert.NotEmpty(ctx.Repository.Evidence);

        // A DPIA is not a once-and-for-all artefact: the processing
        // changes, and so does the balance.
        ctx.Time.Advance(TimeSpan.FromDays(400));

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha2", Human("1", "alex"))], null, true));
        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("review", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Evidence already collected under a valid decision is kept — it
        // was lawfully collected at the time.
        Assert.Single(ctx.Repository.Evidence);
    }

    [Fact]
    public async Task A_withdrawn_decision_blocks_collection_immediately()
    {
        var ctx = WithCommits();
        await ctx.Continuity.WithdrawProcessingDecisionAsync(EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        // Withdrawing leaves no *live* decision, so the admin is told to
        // record one rather than that the old one was withdrawn — which
        // is the more useful instruction, and accurate: after withdrawal
        // there genuinely is no decision in force.
        Assert.Equal(EvidenceProcessingDecision.MissingReason, ex.Message);
        Assert.Empty(ctx.Repository.Evidence);
    }

    /// <summary>
    /// The withdrawn guard on the decision itself is not reachable from
    /// the ingestion path — a withdrawn row is never returned as live —
    /// but <see cref="EvidenceProcessingDecision.PermitsCollectionOn"/>
    /// is public and the history view hands it superseded rows, so it
    /// has to be right for those too.
    /// </summary>
    [Fact]
    public void A_superseded_decision_never_reports_itself_as_permitting_collection()
    {
        var today = DateOnly.FromDateTime(SkillsEvidenceTestContext.Start.UtcDateTime);
        var superseded = Decision(SkillsEvidenceTestContext.TenantA, today) with
        {
            WithdrawnAtUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

        Assert.False(superseded.PermitsCollectionOn(today));
        Assert.Contains("withdrawn", superseded.BlockingReason(today)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task One_tenants_decision_does_not_permit_another_tenants_collection()
    {
        var ctx = WithCommits();
        ctx.BlockCollection(EvidenceTestContext.TenantB);

        // A is permitted, B is not — the gate is per tenant, like
        // everything else in this codebase.
        await ctx.RunAsync(ctx.ConnectionA);
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionB));
    }

    [Fact]
    public async Task Recording_a_decision_supersedes_the_previous_one_and_keeps_it_in_history()
    {
        var ctx = new ContinuityFixture();

        var first = await ctx.RecordAsync(reviewDueOn: ctx.Today.AddMonths(6));
        ctx.Time.Advance(TimeSpan.FromDays(30));
        var second = await ctx.RecordAsync(reviewDueOn: ctx.Today.AddMonths(12));

        var history = await ctx.Repository.GetProcessingDecisionHistoryAsync(SkillsEvidenceTestContext.TenantA);
        Assert.Equal(2, history.Count);
        // At most one live decision — the filtered unique index in the
        // schema, reproduced by the fake.
        Assert.Single(history, d => d.WithdrawnAtUtc is null);
        Assert.Equal(second.DecisionKey, (await ctx.Repository.GetLiveProcessingDecisionAsync(SkillsEvidenceTestContext.TenantA))!.DecisionKey);
        Assert.NotNull(history.Single(d => d.DecisionKey == first.DecisionKey).WithdrawnAtUtc);
    }

    [Fact]
    public async Task A_decision_needs_a_stated_purpose_and_a_future_review_date()
    {
        var ctx = new ContinuityFixture();

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RecordAsync(purpose: "   "));
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RecordAsync(reviewDueOn: ctx.Today.AddDays(-1)));
    }

    [Fact]
    public async Task Recording_and_withdrawing_are_both_audited_with_the_customers_references()
    {
        var ctx = new ContinuityFixture();
        await ctx.RecordAsync();
        await ctx.Service.WithdrawProcessingDecisionAsync(SkillsEvidenceTestContext.TenantA, 7);

        var recorded = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.ProcessingDecisionRecorded);
        // The references are the customer's own artefacts, not free text
        // about a person, so they belong in a log an auditor will read.
        Assert.Contains("DPIA-2026-14", recorded.DetailJson!, StringComparison.Ordinal);
        Assert.Contains(ctx.AuditLog.Entries, e => e.Action == SkillsEvidenceAuditAction.ProcessingDecisionWithdrawn);
    }

    [Fact]
    public async Task The_blocking_reason_names_the_one_thing_that_failed()
    {
        var ctx = new ContinuityFixture();
        var today = ctx.Today;

        var noNotice = Decision(SkillsEvidenceTestContext.TenantA, today, workerNotice: false);
        var noDpia = Decision(SkillsEvidenceTestContext.TenantA, today, dpia: false);
        var expired = Decision(SkillsEvidenceTestContext.TenantA, today) with { ReviewDueOn = today.AddDays(-1) };
        var good = Decision(SkillsEvidenceTestContext.TenantA, today);

        Assert.Contains("not been told", noNotice.BlockingReason(today)!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DPIA", noDpia.BlockingReason(today)!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review", expired.BlockingReason(today)!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(good.BlockingReason(today));
        Assert.True(good.PermitsCollectionOn(today));

        await Task.CompletedTask;
    }

    private static EvidenceProcessingDecision Decision(EvidenceTestContext ctx, bool workerNotice = true, bool dpia = true) =>
        Decision(EvidenceTestContext.TenantA, DateOnly.FromDateTime(ctx.Time.Now.UtcDateTime), workerNotice, dpia);

    private static EvidenceProcessingDecision Decision(Guid tenantId, DateOnly today, bool workerNotice = true, bool dpia = true) => new()
    {
        DecisionKey = Guid.NewGuid(),
        TenantId = tenantId,
        LawfulBasis = EvidenceLawfulBasis.LegitimateInterests,
        WorkerNoticeGiven = workerNotice,
        DpiaCompleted = dpia,
        Purpose = "Finding expertise and planning cover",
        DecidedByStaffKey = Guid.NewGuid(),
        DecidedAtUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        ReviewDueOn = today.AddYears(1)
    };

    /// <summary>Minimal fixture for the decision lifecycle, without an evidence connection.</summary>
    private sealed class ContinuityFixture
    {
        public FakeContinuityRepository Repository { get; } = new();
        public FakeSkillsEvidenceAuditLogRepository AuditLog { get; } = new();
        public FixedTimeProvider Time { get; } = new(SkillsEvidenceTestContext.Start);
        public ContinuityService Service { get; }

        public DateOnly Today => DateOnly.FromDateTime(Time.Now.UtcDateTime);

        public ContinuityFixture() => Service = new ContinuityService(Repository, AuditLog, Time);

        public Task<EvidenceProcessingDecision> RecordAsync(
            string purpose = "Finding expertise and planning cover", DateOnly? reviewDueOn = null) =>
            Service.RecordProcessingDecisionAsync(
                EvidenceLawfulBasis.LegitimateInterests, workerNoticeGiven: true,
                "Staff handbook s.9", dpiaCompleted: true, "DPIA-2026-14", Time.Now.UtcDateTime,
                purpose, reviewDueOn ?? Today.AddYears(1), Guid.NewGuid(), SkillsEvidenceTestContext.TenantA, 7);
    }
}
