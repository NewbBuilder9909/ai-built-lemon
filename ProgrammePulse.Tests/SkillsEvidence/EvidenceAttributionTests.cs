using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Who evidence belongs to, and — mostly — who it does not.
///
/// The single rule these defend: an external account becomes a named
/// person **only** through an explicitly approved link. Everything else
/// goes to a visible queue. Getting this wrong attributes one employee's
/// work to another on a page a manager may read before a conversation
/// about someone's career, which is why the brief forbids display-name
/// and email-only matching outright.
/// </summary>
public class EvidenceAttributionTests
{
    [Fact]
    public async Task An_unapproved_account_is_never_attributed_even_when_its_email_matches_exactly()
    {
        var ctx = new EvidenceTestContext();

        // alex@acme.test is Alex's real, unique address on the roster. An
        // email match alone must not map anyone (Programme Ops follows the
        // same rule since September 2026).
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex", "alex@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var row = Assert.Single(ctx.Repository.Evidence);
        Assert.Null(row.StaffKey);
        Assert.Equal(EvidenceAttributionStatus.Unmapped, row.AttributionStatus);

        // ...but the match is offered to the admin as a suggestion.
        var queued = Assert.Single(ctx.Repository.Unmapped);
        Assert.Equal(UnmappedActorReason.EmailSuggestion, queued.Reason);
        Assert.Equal(ctx.Alex.StaffKey, queued.SuggestedStaffKey);
    }

    [Fact]
    public async Task An_account_with_no_candidate_is_queued_with_no_suggestion()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("9", "contractor", "someone@elsewhere.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var queued = Assert.Single(ctx.Repository.Unmapped);
        Assert.Equal(UnmappedActorReason.NoCandidate, queued.Reason);
        Assert.Null(queued.SuggestedStaffKey);
    }

    [Fact]
    public async Task Two_staff_sharing_an_address_is_ambiguous_and_resolves_to_nobody()
    {
        var ctx = new EvidenceTestContext();
        // A shared team mailbox on two profiles — the case where taking
        // the first match would be a coin flip with someone's name on it.
        ctx.AddStaff("Team Mailbox A", 110, "dev@acme.test", EvidenceTestContext.TenantA);
        ctx.AddStaff("Team Mailbox B", 111, "dev@acme.test", EvidenceTestContext.TenantA);

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("5", "shared", "dev@acme.test"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        Assert.Null(ctx.Repository.Evidence.Single().StaffKey);
        var queued = Assert.Single(ctx.Repository.Unmapped);
        Assert.Equal(UnmappedActorReason.Ambiguous, queued.Reason);
        Assert.Null(queued.SuggestedStaffKey);
        Assert.Equal(1, ctx.Resolver.AmbiguousCount);
    }

    [Fact]
    public async Task An_approved_link_attributes_evidence_already_ingested_without_a_resync()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex")), Commit("sha2", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.All(ctx.Repository.Evidence, e => Assert.Null(e.StaffKey));

        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        Assert.All(ctx.Repository.Evidence, e => Assert.Equal(ctx.Alex.StaffKey, e.StaffKey));
        Assert.All(ctx.Repository.Evidence, e => Assert.Equal(EvidenceAttributionStatus.Mapped, e.AttributionStatus));
    }

    [Fact]
    public async Task A_later_sync_attributes_new_evidence_through_the_existing_link()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        // A fresh resolver, as a new request would get.
        var second = new ProgrammePulse.Services.SkillsEvidence.EvidenceActorResolver(ctx.Repository, ctx.StaffRepository);
        var coordinator = new SyncRunCoordinator(
            new SyncRunGuard(),
            ctx.SyncRuns,
            Microsoft.Extensions.Options.Options.Create(new ProgrammeOpsOptions { SyncLeaseSeconds = 300 }),
            ctx.Time);
        var ingestion = new ProgrammePulse.Services.Integrations.GitHub.GitHubEvidenceIngestionService(
            ctx.Client, ctx.Repository, ctx.Protector, second, ctx.AuditLog, ctx.Continuity, ctx.SyncRuns,
            Microsoft.Extensions.Options.Options.Create(new ProgrammePulse.Services.Integrations.GitHub.SkillsEvidenceOptions()),
            coordinator,
            ctx.Time);

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha2", Human("1", "alex"))], null, true));
        await ingestion.RunAsync(ctx.ConnectionA.ConnectionKey, EvidenceTestContext.TenantA, null);

        Assert.Equal(ctx.Alex.StaffKey, ctx.Repository.Evidence.Single(e => e.ExternalId == "sha2").StaffKey);
    }

    [Fact]
    public async Task Revoking_a_link_unattributes_the_evidence_without_deleting_it()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);
        var link = await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        await ctx.Repository.DeleteActorLinkAsync(link.LinkKey, EvidenceTestContext.TenantA);
        var detached = await ctx.Repository.DetachEvidenceFromStaffAsync(
            ctx.ConnectionA.ConnectionKey, "1", EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        Assert.Equal(1, detached);
        var row = Assert.Single(ctx.Repository.Evidence);
        // The commit really happened. It simply stops being anybody's.
        Assert.Null(row.StaffKey);
        Assert.Equal(EvidenceAttributionStatus.Unmapped, row.AttributionStatus);
    }

    [Fact]
    public async Task A_bot_is_excluded_from_attribution_but_stays_visible_on_the_queue()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Bot("100", "dependabot[bot]"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var row = Assert.Single(ctx.Repository.Evidence);
        Assert.True(row.ActorIsBot);
        Assert.Equal(EvidenceAttributionStatus.Bot, row.AttributionStatus);
        Assert.Null(row.StaffKey);

        // Visible, so an admin can see the exclusion happened and correct
        // it if a real person's account has been caught by the name rules.
        var queued = Assert.Single(ctx.Repository.Unmapped);
        Assert.Equal(UnmappedActorReason.Bot, queued.Reason);
        Assert.True(queued.IsBot);
    }

    [Fact]
    public async Task Bots_do_not_inflate_the_unmapped_backlog_a_human_has_to_clear()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("s1", Bot("100", "dependabot[bot]")),
             Commit("s2", Bot("101", "renovate")),
             Commit("s3", Human("1", "alex"))], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        var coverage = await ctx.Portfolio.BuildCoverageAsync(EvidenceTestContext.TenantA);

        Assert.Equal(1, coverage.UnmappedActors);
    }

    [Theory]
    [InlineData("dependabot[bot]", null)]
    [InlineData("renovate", null)]
    [InlineData("github-actions", null)]
    [InlineData("release-bot", null)]
    [InlineData("anything", "Bot")]
    public void Bot_accounts_are_recognised_by_type_or_by_name(string login, string? accountType) =>
        Assert.True(EvidenceActorClassifier.IsBot(login, accountType));

    [Theory]
    [InlineData("alex")]
    [InlineData("robotics-team")]
    [InlineData("bottomley")]
    public void An_ordinary_login_that_merely_contains_bot_letters_is_not_a_bot(string login) =>
        Assert.False(EvidenceActorClassifier.IsBot(login, "User"));

    [Fact]
    public async Task A_trailer_only_co_author_is_queued_under_a_key_that_approving_actually_attributes()
    {
        var ctx = new EvidenceTestContext();

        // Co-authored-by carries an email and no account, so the mapper
        // and the resolver must agree on the synthetic key — otherwise
        // approving the queue row would attribute nothing.
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"), message: "Pair work\n\nCo-authored-by: Sarah Evans <sarah@acme.test>")],
            null, true));

        await ctx.RunAsync(ctx.ConnectionA);

        var coAuthorRow = ctx.Repository.Evidence.Single(e => e.Role == EvidenceRole.CoAuthor);
        var queued = ctx.Repository.Unmapped.Single(a => a.Email == "sarah@acme.test");
        Assert.Equal(coAuthorRow.ActorExternalId, queued.ExternalActorId);

        await ctx.ApproveAsync(ctx.ConnectionA, queued.ExternalActorId, ctx.Sarah);

        Assert.Equal(ctx.Sarah.StaffKey, ctx.Repository.Evidence.Single(e => e.Role == EvidenceRole.CoAuthor).StaffKey);
    }

    [Fact]
    public async Task A_resolved_account_seen_again_reopens_rather_than_being_absorbed()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        var queued = Assert.Single(ctx.Repository.Unmapped);
        await ctx.Repository.MarkUnmappedResolvedAsync(queued.UnmappedActorKey, EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime);
        Assert.False(ctx.Repository.Unmapped.Single().IsOpen);

        // Seen again with no link in place: whatever resolved it evidently
        // does not cover this actor, and that has to be visible.
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([Commit("sha2", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        Assert.True(ctx.Repository.Unmapped.Single().IsOpen);
        Assert.Equal(2, ctx.Repository.Unmapped.Single().OccurrenceCount);
    }

    [Fact]
    public async Task The_same_account_cannot_be_mapped_to_two_people_on_one_connection()
    {
        var ctx = new EvidenceTestContext();
        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        await Assert.ThrowsAsync<ProgrammePulse.Services.SkillsEvidence.SkillAssertionValidationException>(() =>
            ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Sarah, "alex"));
    }

    [Fact]
    public async Task A_link_against_another_tenants_connection_is_refused()
    {
        var ctx = new EvidenceTestContext();

        await Assert.ThrowsAsync<ProgrammePulse.Services.Shared.CrossTenantReferenceException>(() =>
            ctx.Repository.CreateActorLinkAsync(new EvidenceActorLink
            {
                LinkKey = Guid.NewGuid(),
                TenantId = EvidenceTestContext.TenantA,
                // Tenant B's connection — a real key, the wrong owner.
                ConnectionKey = ctx.ConnectionB.ConnectionKey,
                Provider = "GitHub",
                ExternalActorId = "1",
                StaffKey = ctx.Alex.StaffKey,
                ApprovedAtUtc = ctx.Time.Now.UtcDateTime
            }));
    }
}
