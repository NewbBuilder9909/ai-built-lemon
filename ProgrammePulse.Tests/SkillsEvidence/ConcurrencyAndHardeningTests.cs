using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Freshdesk;
using ProgrammePulse.Services.Integrations.GitHub;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The hardening pass: concurrent syncs, and three defects found by
/// reading back the slices rather than by a failing test.
///
/// None of these was a gap in the brief — they are the things that only
/// become visible once the feature exists and you ask "what happens if
/// two people do this at once", or "what does that number actually
/// count".
/// </summary>
public class ConcurrencyAndHardeningTests
{
    // ---- One sync at a time ----

    [Fact]
    public async Task A_second_evidence_sync_for_the_same_tenant_is_refused_while_one_is_running()
    {
        var ctx = new EvidenceTestContext();

        // Take the latch as an in-flight run would, then attempt a second.
        using var held = ctx.RunGuard.TryEnter(
            EvidenceTestContext.TenantA, GitHubEvidenceIngestionService.SourceName);
        Assert.NotNull(held);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("already running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_latch_is_released_when_a_run_finishes_so_the_next_one_proceeds()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        // Would throw if the first run had not released.
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.Repository.Evidence);
    }

    [Fact]
    public async Task The_latch_is_released_even_when_a_run_fails()
    {
        var ctx = new EvidenceTestContext();
        ctx.Protector.Readable = false;

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        // A latch leaked on the failure path would wedge the tenant's
        // syncs until the process restarted — the worst kind of bug,
        // because it only appears after something else has gone wrong.
        ctx.Protector.Readable = true;
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.Repository.Evidence);
    }

    [Fact]
    public async Task Two_tenants_never_serialize_against_each_other()
    {
        var ctx = new EvidenceTestContext();

        using var held = ctx.RunGuard.TryEnter(
            EvidenceTestContext.TenantA, GitHubEvidenceIngestionService.SourceName);

        // Tenant B is unaffected: the latch is keyed on (tenant, source),
        // so one customer's sync never blocks another's.
        ctx.Client.WithCommits(EvidenceTestContext.RepoB, new GitHubPage<GitHubCommit>(
            [Commit("shb1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionB);

        Assert.Single(ctx.Repository.Evidence);
    }

    [Fact]
    public async Task A_desk_sync_and_an_evidence_sync_do_not_block_each_other()
    {
        var evidence = new EvidenceTestContext();

        // Different source names, so holding one says nothing about the
        // other. Checked because a shared latch key would make a desk
        // sync wait on a repository sync for no reason.
        using var held = evidence.RunGuard.TryEnter(
            EvidenceTestContext.TenantA, GitHubEvidenceIngestionService.SourceName);

        Assert.NotNull(evidence.RunGuard.TryEnter(
            EvidenceTestContext.TenantA, FreshdeskIngestionService.SourceName));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_successful_evidence_sync_records_a_durable_run_summary()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var run = ctx.SyncRuns.Single(GitHubEvidenceIngestionService.SourceName);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Contains("evidence rows", run.Summary);
    }

    [Fact]
    public async Task A_live_database_lease_on_another_instance_refuses_a_new_evidence_sync()
    {
        var ctx = new EvidenceTestContext();
        ctx.SyncRuns.Leases[(EvidenceTestContext.TenantA, GitHubEvidenceIngestionService.SourceName)] =
            ("other-instance", EvidenceTestContext.Start.UtcDateTime.AddMinutes(5), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("another instance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(ctx.RunGuard.IsRunning(EvidenceTestContext.TenantA, GitHubEvidenceIngestionService.SourceName));
    }

    [Fact]
    public async Task A_second_desk_sync_for_the_same_tenant_is_refused()
    {
        var ctx = new ServiceOps.ServiceOpsTestContext();

        using var held = ctx.RunGuard.TryEnter(
            ServiceOps.ServiceOpsTestContext.TenantA, FreshdeskIngestionService.SourceName);

        var ex = await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.RunAsync());

        // It matters more here than for evidence: the watermark is
        // rewound by an overlap every run, so two concurrent runs would
        // race on the cursor as well as on the rows.
        Assert.Contains("already running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Unidentifiable sightings are not unidentified accounts ----

    [Fact]
    public async Task A_sighting_with_nothing_to_key_on_is_counted_separately_from_unmapped_accounts()
    {
        var ctx = new EvidenceTestContext();

        // A commit whose author has no account id, no login and no email.
        // It cannot reach the mapping queue, so counting it as an
        // "unidentified account" would overstate a backlog an admin
        // cannot clear.
        var anonymous = new GitHubActor(Id: null, Login: null, Type: null, Name: "Somebody", Email: null);
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", anonymous), Commit("sha2", anonymous)], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Equal(0, ctx.Resolver.UnmappedCount);
        Assert.Equal(2, ctx.Resolver.UnidentifiableSightings);
        Assert.Empty(ctx.Repository.Unmapped);

        // The readiness and coverage figures count the queue, which is
        // empty — and correctly so, because there is nothing to approve.
        var coverage = await ctx.Portfolio.BuildCoverageAsync(EvidenceTestContext.TenantA);
        Assert.Equal(0, coverage.UnmappedActors);
    }

    [Fact]
    public async Task The_same_account_seen_many_times_is_still_one_unidentified_account()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
        [
            Commit("sha1", Human("9", "contractor")),
            Commit("sha2", Human("9", "contractor")),
            Commit("sha3", Human("9", "contractor"))
        ], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        // The number an admin sees is accounts to work through, not
        // sightings.
        Assert.Equal(1, ctx.Resolver.UnmappedCount);
        Assert.Equal(3, ctx.Repository.Unmapped.Single().OccurrenceCount);
    }

    // ---- No Guid.Empty sentinel for an unassigned action ----

    [Fact]
    public async Task An_erased_owner_leaves_the_action_unassigned_rather_than_owned_by_an_empty_guid()
    {
        var ctx = new ContinuityFixture();
        var owner = Guid.NewGuid();

        await ctx.Service.DeclareComponentAsync("billing", "Billing", null, null, ctx.Reviewer, Tenant, 1);
        var action = await ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Training, owner, "needs training", null, null, ctx.Reviewer, Tenant, 1);

        await ctx.Repository.DetachStaffAsync(owner, ctx.Time.Now.UtcDateTime);

        var detached = ctx.Repository.Actions.Single(a => a.ActionKey == action.ActionKey);
        // Null, not Guid.Empty: an empty Guid reads as a real value in a
        // join, and the first query that forgets the convention treats
        // "unassigned" as a person.
        Assert.Null(detached.OwnerStaffKey);
        Assert.True(detached.NeedsReassignment);
        Assert.Equal("needs training", detached.Rationale);
    }

    [Fact]
    public async Task An_action_still_cannot_be_raised_without_an_owner()
    {
        var ctx = new ContinuityFixture();
        await ctx.Service.DeclareComponentAsync("billing", "Billing", null, null, ctx.Reviewer, Tenant, 1);

        // Losing an owner to an erasure is allowed; never having one is not.
        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.Service.RaiseActionAsync(
            "billing", CoverageActionType.Pair, Guid.Empty, "no owner", null, null, ctx.Reviewer, Tenant, 1));
    }

    private static Guid Tenant => SkillsEvidenceTestContext.TenantA;

    private sealed class ContinuityFixture
    {
        public FakeContinuityRepository Repository { get; } = new();
        public FakeSkillsEvidenceAuditLogRepository AuditLog { get; } = new();
        public FixedTimeProvider Time { get; } = new(SkillsEvidenceTestContext.Start);
        public ContinuityService Service { get; }
        public Guid Reviewer { get; } = Guid.NewGuid();

        public ContinuityFixture() => Service = new ContinuityService(Repository, AuditLog, Time);
    }
}
