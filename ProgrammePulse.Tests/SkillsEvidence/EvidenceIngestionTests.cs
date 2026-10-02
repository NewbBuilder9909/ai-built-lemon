using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Ingestion: replay, partial pages, permission loss, force-push, squash
/// merges, bots and multi-tenant separation.
///
/// These are the cases the Slice 2 brief names, and every one of them is
/// a case that cannot be produced on demand against a real repository —
/// which is exactly why they are fixtures. A connector tested only on the
/// happy path is a connector whose failure modes are unknown.
/// </summary>
public class EvidenceIngestionTests
{
    [Fact]
    public async Task A_commit_produces_one_row_per_person_per_role()
    {
        var ctx = new EvidenceTestContext();
        var author = Human("1", "alex");
        var committer = Human("2", "sarah");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", author, committer, "Fix the mapper\n\nCo-authored-by: Nia Roberts <nia@acme.test>")],
            null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var rows = ctx.Repository.Evidence;
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Role == EvidenceRole.CommitAuthor && r.ActorLogin == "alex");
        // The committer is a separate claim: they applied it, which is
        // often a rebase or a merge, not authorship.
        Assert.Contains(rows, r => r.Role == EvidenceRole.CommitCommitter && r.ActorLogin == "sarah");
        Assert.Contains(rows, r => r.Role == EvidenceRole.CoAuthor && r.ActorEmail == "nia@acme.test");
    }

    [Fact]
    public async Task A_direct_commit_does_not_double_count_the_same_person_as_author_and_committer()
    {
        var ctx = new EvidenceTestContext();
        var alex = Human("1", "alex");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", alex, alex)], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Single(ctx.Repository.Evidence);
        Assert.Equal(EvidenceRole.CommitAuthor, ctx.Repository.Evidence[0].Role);
    }

    [Fact]
    public async Task A_squash_merge_keeps_the_author_and_the_squashing_committer_apart()
    {
        var ctx = new EvidenceTestContext();
        var author = Human("1", "alex");
        var mergeBot = Bot("99", "github-actions[bot]");

        // The shape a squash merge actually produces: one commit, written
        // by a person, applied by the merge queue.
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("squashed", author, mergeBot, "Add billing export (#42)")], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var authorRow = ctx.Repository.Evidence.Single(r => r.Role == EvidenceRole.CommitAuthor);
        var committerRow = ctx.Repository.Evidence.Single(r => r.Role == EvidenceRole.CommitCommitter);

        Assert.False(authorRow.ActorIsBot);
        Assert.True(committerRow.ActorIsBot);
        // The human keeps the language hints; the bot that applied the
        // squash is not credited with participating in the files.
        Assert.Empty(committerRow.LanguageHints);
    }

    [Fact]
    public async Task Replaying_the_same_page_updates_rows_rather_than_duplicating_them()
    {
        var ctx = new EvidenceTestContext();
        var alex = Human("1", "alex");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", alex)], null, true));
        await ctx.RunAsync(ctx.ConnectionA);
        var firstIngested = ctx.Repository.Evidence.Single().FirstIngestedAtUtc;

        ctx.Time.Advance(TimeSpan.FromHours(2));
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", alex)], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        var row = Assert.Single(ctx.Repository.Evidence);
        // First-seen is preserved across replays; updated moves. Without
        // that, a replay would rewrite the observation window.
        Assert.Equal(firstIngested, row.FirstIngestedAtUtc);
        Assert.True(row.UpdatedAtUtc > firstIngested);
    }

    [Fact]
    public async Task A_force_push_that_rewrites_a_sha_adds_the_new_commit_and_keeps_the_old_one()
    {
        var ctx = new EvidenceTestContext();
        var alex = Human("1", "alex");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("before", alex)], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        // A rebase rewrites history: the same work reappears under a new
        // SHA. We cannot tell that from a genuinely new commit, and
        // guessing would be worse than over-counting — so both are kept
        // and the ambiguity is a known limitation, not a silent merge.
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("after", alex)], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Equal(2, ctx.Repository.Evidence.Count);
        Assert.Contains(ctx.Repository.Evidence, r => r.ExternalId == "before");
        Assert.Contains(ctx.Repository.Evidence, r => r.ExternalId == "after");
    }

    [Fact]
    public async Task A_partial_page_keeps_what_it_fetched_and_does_not_advance_the_coverage_claim()
    {
        var ctx = new EvidenceTestContext();
        var alex = Human("1", "alex");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", alex)], NextCursor: null, IsComplete: false, IncompleteReason: "the GitHub rate limit was reached"));

        await ctx.RunAsync(ctx.ConnectionA);

        // The row it managed to fetch is kept — a partial run must never
        // delete previously known facts, nor discard new ones.
        Assert.Contains(ctx.Repository.Evidence, r => r.ExternalId == "sha1");

        var coverage = ctx.Repository.Coverage.Single(c => c.Stream == EvidenceStream.Commits);
        Assert.Equal(EvidenceCoverageStatus.Partial, coverage.Status);
        // The claim does not move. This is what keeps "we have no evidence"
        // distinguishable from "we have not looked".
        Assert.Null(coverage.CompleteThroughUtc);
        Assert.Contains("rate limit", coverage.StatusDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_complete_run_advances_the_coverage_claim()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], "2026-09-20T09:00:00Z", true));

        await ctx.RunAsync(ctx.ConnectionA);

        var coverage = ctx.Repository.Coverage.Single(c => c.Stream == EvidenceStream.Commits);
        Assert.Equal(EvidenceCoverageStatus.Complete, coverage.Status);
        Assert.Equal(ctx.Time.Now.UtcDateTime, coverage.CompleteThroughUtc);
        Assert.Equal("2026-09-20T09:00:00Z", coverage.Cursor);
    }

    [Fact]
    public async Task A_later_run_resumes_from_the_stored_cursor()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([], "cursor-1", true));
        await ctx.RunAsync(ctx.ConnectionA);

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([], "cursor-2", true));
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Equal([null, "cursor-1"], ctx.Client.ObservedCommitCursors);
    }

    [Fact]
    public async Task Losing_permission_marks_the_repository_rather_than_deleting_its_evidence()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);
        Assert.NotEmpty(ctx.Repository.Evidence);

        // The installation loses access to the repository between runs.
        ctx.Client.AccessLost.Add(EvidenceTestContext.RepoA);
        await ctx.RunAsync(ctx.ConnectionA);

        // Evidence collected while we had permission was true then and
        // stays. Only the claim to current coverage is withdrawn.
        Assert.NotEmpty(ctx.Repository.Evidence);
        Assert.All(
            ctx.Repository.Coverage.Where(c => c.Stream != EvidenceStream.Reviews),
            c => Assert.Equal(EvidenceCoverageStatus.PermissionLost, c.Status));
    }

    [Fact]
    public async Task A_connection_that_can_read_nothing_is_marked_access_lost()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.AccessLost.Add(EvidenceTestContext.RepoA);

        await ctx.RunAsync(ctx.ConnectionA);

        var connection = ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.ConnectionA.ConnectionKey);
        Assert.Equal(EvidenceConnectionStatus.AccessLost, connection.Status);
        // The credential is kept so reconnecting is a re-authorisation,
        // not a re-installation — but nothing will sync until it is.
        Assert.NotNull(connection.ProtectedCredentialJson);
    }

    [Fact]
    public async Task A_repository_dropped_from_the_selection_goes_out_of_scope_and_keeps_its_evidence()
    {
        var ctx = new EvidenceTestContext();
        var second = "acme-ltd/api";
        ctx.Repository.Connections[0] = ctx.ConnectionA with { SelectedRepositories = [EvidenceTestContext.RepoA, second] };

        ctx.Client.WithCommits(second, new GitHubPage<GitHubCommit>([Commit("sha-api", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.Repository.Connections[0]);
        Assert.Contains(ctx.Repository.Evidence, e => e.RepositoryKey == second);

        // The admin narrows the selection.
        ctx.Repository.Connections[0] = ctx.ConnectionA with { SelectedRepositories = [EvidenceTestContext.RepoA] };
        await ctx.RunAsync(ctx.Repository.Connections[0]);

        Assert.Contains(ctx.Repository.Evidence, e => e.RepositoryKey == second);
        Assert.All(
            ctx.Repository.Coverage.Where(c => c.RepositoryKey == second),
            c => Assert.Equal(EvidenceCoverageStatus.OutOfScope, c.Status));
    }

    [Fact]
    public async Task A_run_without_a_readable_credential_stops_rather_than_falling_back()
    {
        var ctx = new EvidenceTestContext();
        // A rotated Data Protection key ring, or tampering.
        ctx.Protector.Readable = false;

        var ex = await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));

        Assert.Contains("no shared fallback", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ctx.Repository.Evidence);
    }

    [Fact]
    public async Task A_disconnected_connection_refuses_to_sync()
    {
        var ctx = new EvidenceTestContext();
        await ctx.Repository.SetConnectionStatusAsync(
            ctx.ConnectionA.ConnectionKey, EvidenceConnectionStatus.Disconnected,
            EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime, clearCredential: true);

        await Assert.ThrowsAsync<SkillAssertionValidationException>(() => ctx.RunAsync(ctx.ConnectionA));
    }

    [Fact]
    public async Task Another_tenants_connection_key_reads_as_not_found()
    {
        var ctx = new EvidenceTestContext();

        // A real connection key, in the wrong tenant.
        await Assert.ThrowsAsync<ProgrammePulse.Services.Shared.CrossTenantReferenceException>(() =>
            ctx.Ingestion.RunAsync(ctx.ConnectionB.ConnectionKey, EvidenceTestContext.TenantA, null));
    }

    [Fact]
    public async Task Two_tenants_syncing_the_same_login_keep_their_evidence_and_mappings_apart()
    {
        var ctx = new EvidenceTestContext();
        // The same person's GitHub account appears in both organisations.
        var shared = Human("1", "alex");

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha-a", shared)], null, true));
        ctx.Client.WithCommits(EvidenceTestContext.RepoB, new GitHubPage<GitHubCommit>([Commit("sha-b", shared)], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.RunAsync(ctx.ConnectionB);

        // Tenant A approves the mapping. Tenant B has not, and must not
        // inherit it — approving someone in one organisation is not
        // vouching for an identically named account in another.
        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        var attributedInA = ctx.Repository.Evidence.Where(e => e.TenantId == EvidenceTestContext.TenantA && e.StaffKey is not null).ToList();
        var attributedInB = ctx.Repository.Evidence.Where(e => e.TenantId == EvidenceTestContext.TenantB && e.StaffKey is not null).ToList();

        Assert.Single(attributedInA);
        Assert.Empty(attributedInB);
    }

    [Fact]
    public async Task Bronze_captures_are_written_for_every_artefact()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", Human("1", "alex"))], null, true));
        ctx.Client.WithPullRequests(EvidenceTestContext.RepoA, new GitHubPage<GitHubPullRequest>(
            [PullRequest("pr1", 1, Human("1", "alex"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Contains(ctx.Repository.Raw, r => r.EntityType == "commit" && r.ExternalId == "sha1");
        Assert.Contains(ctx.Repository.Raw, r => r.EntityType == "pull_request" && r.ExternalId == "pr1");
    }

    [Fact]
    public async Task A_sync_is_audited_with_its_counts()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", Human("1", "alex"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var entry = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.EvidenceSyncCompleted);
        Assert.Equal(EvidenceTestContext.TenantA, entry.TenantId);
        Assert.Contains("\"rows\":1", entry.DetailJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviews_are_recorded_against_the_reviewer_not_the_pull_request_author()
    {
        var ctx = new EvidenceTestContext();
        var author = Human("1", "alex");
        var reviewer = Human("2", "sarah");

        ctx.Client.WithPullRequests(EvidenceTestContext.RepoA, new GitHubPage<GitHubPullRequest>(
            [PullRequest("pr1", 1, author)], null, true));
        ctx.Client.WithReviews(EvidenceTestContext.RepoA, new GitHubPage<GitHubReview>(
            [Review("rev1", 1, reviewer)], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var authorRow = ctx.Repository.Evidence.Single(e => e.Role == EvidenceRole.PullRequestAuthor);
        var reviewRow = ctx.Repository.Evidence.Single(e => e.Role == EvidenceRole.Reviewer);

        Assert.Equal("alex", authorRow.ActorLogin);
        Assert.Equal("sarah", reviewRow.ActorLogin);
        // A reviewer did not write the code, and the row says so rather
        // than being folded into contribution.
        Assert.Equal(EvidenceSourceType.Review, reviewRow.SourceType);
    }

    [Fact]
    public async Task An_incomplete_pull_request_page_makes_its_reviews_partial_too()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithPullRequests(EvidenceTestContext.RepoA, new GitHubPage<GitHubPullRequest>(
            [PullRequest("pr1", 1, Human("1", "alex"))], null, false, "page limit reached"));
        ctx.Client.WithReviews(EvidenceTestContext.RepoA, new GitHubPage<GitHubReview>(
            [Review("rev1", 1, Human("2", "sarah"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        // Reviews hang off the PRs this run saw, so an incomplete PR page
        // means the review set is incomplete whatever the review call said.
        var reviews = ctx.Repository.Coverage.Single(c => c.Stream == EvidenceStream.Reviews);
        Assert.Equal(EvidenceCoverageStatus.Partial, reviews.Status);
    }
}
