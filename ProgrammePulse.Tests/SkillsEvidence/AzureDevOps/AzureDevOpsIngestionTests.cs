using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.AzureDevOps.FakeAzureDevOpsEvidenceClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// Azure DevOps ingestion against the shared evidence contract. Every
/// guarantee the GitHub connector makes — one row per person per role,
/// idempotent replay, honest partial coverage, no deletion on lost access,
/// the processing-decision gate, no credential fallback, tenant isolation —
/// is re-asserted here for the second forge, plus the two things Azure
/// DevOps does differently: commit actors have no account id, and reviews
/// are pull-request votes.
/// </summary>
public sealed class AzureDevOpsIngestionTests
{
    private const string RepoA = AzureDevOpsTestContext.RepoA;

    // ---- attribution shape ----

    [Fact]
    public async Task A_commit_produces_one_row_per_person_per_role_keyed_by_email()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>(
        [
            Commit("c1", Git("alex@acme.test", "Alex"), Git("sarah@acme.test", "Sarah"),
                "Fix invoice rounding\n\nCo-authored-by: Rhian <rhian@acme.test>")
        ], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var rows = ctx.EvidenceFor(ctx.ConnectionA);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Role == EvidenceRole.CommitAuthor && r.ActorExternalId == "email:alex@acme.test");
        Assert.Contains(rows, r => r.Role == EvidenceRole.CommitCommitter && r.ActorExternalId == "email:sarah@acme.test");
        Assert.Contains(rows, r => r.Role == EvidenceRole.CoAuthor && r.ActorExternalId == "email:rhian@acme.test");
        Assert.All(rows, r =>
        {
            Assert.Equal("AzureDevOps", r.Provider);
            Assert.Equal("Fix invoice rounding", r.Title);
            Assert.Equal(RepoA, r.RepositoryKey);
        });
    }

    [Fact]
    public async Task A_direct_commit_does_not_count_the_same_email_as_author_and_committer()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>(
            [Commit("c1", Git("alex@acme.test"), Git("ALEX@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
    }

    [Fact]
    public async Task An_email_match_is_only_a_suggestion_until_an_admin_approves_it()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>(
            [Commit("c1", Git("alex@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var row = Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
        Assert.Null(row.StaffKey);
        Assert.Equal(EvidenceAttributionStatus.Unmapped, row.AttributionStatus);

        var queued = Assert.Single(ctx.Evidence.Repository.Unmapped, u => u.ConnectionKey == ctx.ConnectionA.ConnectionKey);
        Assert.Equal(UnmappedActorReason.EmailSuggestion, queued.Reason);
        Assert.Equal(ctx.Evidence.Alex.StaffKey, queued.SuggestedStaffKey);

        // Approving the queue row's key attributes the row already stored,
        // which only works if mapper and resolver agree on that key.
        await ctx.Evidence.ApproveAsync(ctx.ConnectionA, queued.ExternalActorId, ctx.Evidence.Alex);
        Assert.Equal(ctx.Evidence.Alex.StaffKey, Assert.Single(ctx.EvidenceFor(ctx.ConnectionA)).StaffKey);
    }

    [Fact]
    public async Task Reviews_come_from_votes_and_skip_silent_and_group_reviewers()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithPullRequests(RepoA, new AzureDevOpsPage<AzureDevOpsPullRequest>(
        [
            PullRequest(41, Account("u-alex", "alex@acme.test"),
                new AzureDevOpsReviewer(Account("u-sarah", "sarah@acme.test"), 10),
                new AzureDevOpsReviewer(Account("u-rhian", "rhian@acme.test"), -10),
                new AzureDevOpsReviewer(Account("u-idle", "idle@acme.test"), 0),
                new AzureDevOpsReviewer(Account("g-web", "[Web]\\Web Team", "Web Team", isContainer: true), 10))
        ], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var rows = ctx.EvidenceFor(ctx.ConnectionA);
        Assert.Equal(3, rows.Count);

        var author = Assert.Single(rows, r => r.Role == EvidenceRole.PullRequestAuthor);
        Assert.Equal("u-alex", author.ActorExternalId);
        Assert.Equal("41", author.ExternalId);

        var reviews = rows.Where(r => r.Role == EvidenceRole.Reviewer).ToList();
        Assert.Equal(2, reviews.Count);
        Assert.Contains(reviews, r => r.ActorExternalId == "u-sarah" && r.Title == "Review (approved) on !41" && r.ExternalId == "41:u-sarah");
        Assert.Contains(reviews, r => r.ActorExternalId == "u-rhian" && r.Title == "Review (rejected) on !41");
        Assert.DoesNotContain(rows, r => r.ActorExternalId is "u-idle" or "g-web");
    }

    [Fact]
    public async Task Build_service_identities_are_bots_and_reach_nobody()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithPullRequests(RepoA, new AzureDevOpsPage<AzureDevOpsPullRequest>(
            [PullRequest(7, BuildService())], null, true));

        var result = await ctx.RunAsync(ctx.ConnectionA);

        var row = Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
        Assert.True(row.ActorIsBot);
        Assert.Equal(EvidenceAttributionStatus.Bot, row.AttributionStatus);
        Assert.False(row.IsAttributableToAPerson);
        Assert.Equal(1, result.BotActorsExcluded);

        // Excluded, not hidden: the exclusion is visible on the queue.
        Assert.Contains(ctx.Evidence.Repository.Unmapped, u => u.IsBot && u.Reason == UnmappedActorReason.Bot);
    }

    // ---- idempotency and coverage honesty ----

    [Fact]
    public async Task Replaying_the_same_page_updates_rows_rather_than_duplicating_them()
    {
        var ctx = new AzureDevOpsTestContext();
        var page = new AzureDevOpsPage<AzureDevOpsCommit>([Commit("c1", Git("alex@acme.test"))], null, true);
        ctx.Client.WithCommits(RepoA, page).WithCommits(RepoA, page);

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
    }

    [Fact]
    public async Task A_partial_page_keeps_what_it_fetched_and_does_not_advance_the_coverage_claim()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>(
            [Commit("c1", Git("alex@acme.test"))], null, false, "Azure DevOps rate limited the run"));

        var result = await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
        var coverage = ctx.Coverage(ctx.ConnectionA, EvidenceStream.Commits)!;
        Assert.Equal(EvidenceCoverageStatus.Partial, coverage.Status);
        Assert.Null(coverage.CompleteThroughUtc);
        Assert.Equal("Azure DevOps rate limited the run", coverage.StatusDetail);
        Assert.Equal(1, result.StreamsPartial);
    }

    [Fact]
    public async Task An_incomplete_pull_request_page_makes_its_reviews_partial_too()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithPullRequests(RepoA, new AzureDevOpsPage<AzureDevOpsPullRequest>(
            [PullRequest(1, Account("u-alex", "alex@acme.test"))], null, false, "the run was cancelled or timed out"));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Equal(EvidenceCoverageStatus.Partial, ctx.Coverage(ctx.ConnectionA, EvidenceStream.PullRequests)!.Status);
        var reviews = ctx.Coverage(ctx.ConnectionA, EvidenceStream.Reviews)!;
        Assert.Equal(EvidenceCoverageStatus.Partial, reviews.Status);
        Assert.Null(reviews.CompleteThroughUtc);
    }

    [Fact]
    public async Task A_complete_run_advances_the_claim_and_the_next_run_resumes_from_the_cursor()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>(
            [Commit("c1", Git("alex@acme.test"))], "2026-09-01T09:00:00.0000000Z", true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.RunAsync(ctx.ConnectionA);

        var coverage = ctx.Coverage(ctx.ConnectionA, EvidenceStream.Commits)!;
        Assert.Equal(EvidenceCoverageStatus.Complete, coverage.Status);
        Assert.Equal(EvidenceTestContext.Start.UtcDateTime, coverage.CompleteThroughUtc);
        Assert.Equal([null, "2026-09-01T09:00:00.0000000Z"], ctx.Client.ObservedCommitCursors);
    }

    [Fact]
    public async Task Losing_access_marks_coverage_and_the_connection_but_keeps_collected_evidence()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>([Commit("c1", Git("alex@acme.test"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        ctx.Client.AccessLost.Add(RepoA);
        var result = await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.EvidenceFor(ctx.ConnectionA));
        Assert.Equal(EvidenceCoverageStatus.PermissionLost, ctx.Coverage(ctx.ConnectionA, EvidenceStream.Commits)!.Status);
        Assert.Equal(EvidenceCoverageStatus.PermissionLost, ctx.Coverage(ctx.ConnectionA, EvidenceStream.Reviews)!.Status);
        Assert.Equal(2, result.StreamsPermissionLost);
        Assert.Equal(EvidenceConnectionStatus.AccessLost,
            ctx.Evidence.Repository.Connections.Single(c => c.ConnectionKey == ctx.ConnectionA.ConnectionKey).Status);
        Assert.Contains(ctx.Evidence.AuditLog.Entries, e => e.Action == SkillsEvidenceAuditAction.EvidenceSyncFailed);
    }

    // ---- refusals ----

    [Fact]
    public async Task Without_a_processing_decision_nothing_is_fetched()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Evidence.BlockCollection(EvidenceTestContext.TenantA);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Equal(EvidenceProcessingDecision.MissingReason, ex.Message);
        Assert.Equal(0, ctx.Client.Calls);
    }

    [Fact]
    public async Task An_expired_token_stops_the_run_and_names_the_date()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Protector.ExpiresAtUtc = EvidenceTestContext.Start.AddDays(-1);

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("expired on 2026-09-19", ex.Message);
        Assert.Equal(0, ctx.Client.Calls);
    }

    [Fact]
    public async Task An_unreadable_credential_stops_rather_than_falling_back()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Protector.Readable = false;

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));
        Assert.Equal(0, ctx.Client.Calls);
    }

    [Fact]
    public async Task Another_tenants_connection_key_reads_as_not_found()
    {
        var ctx = new AzureDevOpsTestContext();

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() =>
            ctx.Ingestion.RunAsync(ctx.ConnectionB.ConnectionKey, EvidenceTestContext.TenantA, triggeredByMemberId: 1));
    }

    [Fact]
    public async Task Each_forge_refuses_the_others_connection()
    {
        var ctx = new AzureDevOpsTestContext();

        var adoOnGitHub = await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Ingestion.RunAsync(ctx.Evidence.ConnectionA.ConnectionKey, EvidenceTestContext.TenantA, 1));
        Assert.Equal("This is not an Azure DevOps connection.", adoOnGitHub.Message);

        var gitHubOnAdo = await Assert.ThrowsAsync<SkillAssertionValidationException>(() =>
            ctx.Evidence.Ingestion.RunAsync(ctx.ConnectionA.ConnectionKey, EvidenceTestContext.TenantA, 1));
        Assert.Equal("This is not a GitHub connection.", gitHubOnAdo.Message);
    }

    // ---- isolation and the proficiency firewall ----

    [Fact]
    public async Task Two_tenants_seeing_the_same_email_keep_their_evidence_and_queues_apart()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>([Commit("c1", Git("shared@contractor.test"))], null, true));
        ctx.Client.WithCommits(AzureDevOpsTestContext.RepoB, new AzureDevOpsPage<AzureDevOpsCommit>([Commit("c1", Git("shared@contractor.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.RunAsync(ctx.ConnectionB);

        Assert.All(ctx.EvidenceFor(ctx.ConnectionA), r => Assert.Equal(EvidenceTestContext.TenantA, r.TenantId));
        Assert.All(ctx.EvidenceFor(ctx.ConnectionB), r => Assert.Equal(EvidenceTestContext.TenantB, r.TenantId));
        Assert.Equal(2, ctx.Evidence.Repository.Unmapped.Count(u => u.ExternalActorId == "email:shared@contractor.test"));
    }

    [Fact]
    public async Task Ingesting_azure_devops_evidence_creates_no_skill_assertion()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithPullRequests(RepoA, new AzureDevOpsPage<AzureDevOpsPullRequest>(
            [PullRequest(1, Account("u-alex", "alex@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.Evidence.ApproveAsync(ctx.ConnectionA, "u-alex", ctx.Evidence.Alex);

        Assert.Empty(ctx.Evidence.SkillsRepository.Assertions);
    }

    [Fact]
    public async Task Bronze_is_captured_under_the_azure_devops_provider()
    {
        var ctx = new AzureDevOpsTestContext();
        ctx.Client.WithCommits(RepoA, new AzureDevOpsPage<AzureDevOpsCommit>([Commit("c1", Git("alex@acme.test"))], null, true));
        ctx.Client.WithPullRequests(RepoA, new AzureDevOpsPage<AzureDevOpsPullRequest>([PullRequest(9, Account("u-alex", "alex@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Contains(ctx.Evidence.Repository.Raw, r => r.EntityType == "commit" && r.ExternalId == "c1");
        Assert.Contains(ctx.Evidence.Repository.Raw, r => r.EntityType == "pull_request" && r.ExternalId == "9");
        Assert.Contains(ctx.Evidence.AuditLog.Entries,
            e => e.Action == SkillsEvidenceAuditAction.EvidenceSyncCompleted && e.DetailJson!.Contains("AzureDevOps"));
    }
}
