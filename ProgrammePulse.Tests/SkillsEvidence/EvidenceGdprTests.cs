using System.Text.Json;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// GDPR propagation into engineering evidence.
///
/// The decision under test: evidence, like a skill assertion, is deleted
/// rather than pseudonymised, along with the approved links that
/// attributed it. What is deliberately left behind is the *unattributed*
/// remainder — rows whose account nobody had mapped, which were never
/// personal data about an identified person here and stay unidentified.
/// The repository's own history still records the commit; this product
/// simply no longer says who made it.
/// </summary>
public class EvidenceGdprTests
{
    private static async Task<EvidenceTestContext> WithAttributedEvidenceAsync()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
        [
            Commit("sha1", Human("1", "alex"), paths: ["src/A.cs", "src/B.cs"]),
            Commit("sha2", Human("1", "alex")),
            Commit("sha3", Human("7", "unknown-contractor"))
        ], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");
        return ctx;
    }

    [Fact]
    public async Task The_export_includes_the_subjects_evidence_and_the_links_that_attributed_it()
    {
        var ctx = await WithAttributedEvidenceAsync();

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);

        var evidenceRows = rows.Where(r => r.Section == SkillsEvidenceDataParticipant.EvidenceSection).ToList();

        // Two commits plus the approval that made them theirs. The link is
        // their data too: it is a statement that an account is them, and
        // they are entitled to see it and dispute it.
        Assert.Equal(3, evidenceRows.Count);
        Assert.Contains(evidenceRows, r => r.Summary.Contains("was approved as this person", StringComparison.Ordinal));
        Assert.Contains(evidenceRows, r => r.Summary.Contains("acme-ltd/web", StringComparison.Ordinal));
        // With a link back to source, so the subject can check it.
        Assert.Contains(evidenceRows, r => r.Summary.Contains("https://github.com/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_export_never_includes_evidence_belonging_to_someone_else()
    {
        var ctx = await WithAttributedEvidenceAsync();

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);

        // sha3 belongs to an account nobody mapped; it is not Alex's.
        Assert.DoesNotContain(rows, r => r.Summary.Contains("sha3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Erasure_deletes_the_subjects_evidence_and_their_approved_links()
    {
        var ctx = await WithAttributedEvidenceAsync();
        Assert.Equal(2, ctx.Repository.Evidence.Count(e => e.StaffKey == ctx.Alex.StaffKey));
        Assert.Single(ctx.Repository.ActorLinks);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.DoesNotContain(ctx.Repository.Evidence, e => e.StaffKey == ctx.Alex.StaffKey);
        Assert.Empty(ctx.Repository.ActorLinks);
        Assert.Empty(await ctx.Participant.ExportAsync(ctx.Alex.StaffKey));
    }

    [Fact]
    public async Task Erasure_leaves_the_unattributed_remainder_which_was_never_about_an_identified_person()
    {
        var ctx = await WithAttributedEvidenceAsync();

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        // sha3's account was never mapped to anyone, so the row was not
        // personal data about an identified person in this system and
        // remains unidentified after the erasure.
        var remaining = Assert.Single(ctx.Repository.Evidence);
        Assert.Equal("sha3", remaining.ExternalId);
        Assert.Null(remaining.StaffKey);
    }

    [Fact]
    public async Task Erasure_also_removes_what_the_subjects_accounts_left_unattributed_and_their_bronze_pages()
    {
        var ctx = await WithAttributedEvidenceAsync();

        // A row under Alex's account that was never attributed (ingested
        // while the link was being approved) still carries the login.
        var attributed = ctx.Repository.Evidence.First(e => e.StaffKey == ctx.Alex.StaffKey);
        ctx.Repository.Evidence.Add(attributed with
        {
            EvidenceKey = Guid.NewGuid(), ExternalId = "sha9", StaffKey = null, AttributionStatus = EvidenceAttributionStatus.Unmapped
        });
        Assert.Contains(ctx.Repository.Raw, r => r.ExternalId == "sha1");

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.DoesNotContain(ctx.Repository.Evidence, e => e.ActorExternalId == attributed.ActorExternalId);
        Assert.DoesNotContain(ctx.Repository.Unmapped, u => u.ExternalActorId == attributed.ActorExternalId);
        Assert.DoesNotContain(ctx.Repository.Raw, r => r.ExternalId is "sha1" or "sha2" or "sha9");
        // The unknown contractor's commit and its page are not Alex's and stay.
        Assert.Contains(ctx.Repository.Evidence, e => e.ExternalId == "sha3");
        Assert.Contains(ctx.Repository.Raw, r => r.ExternalId == "sha3");
    }

    [Fact]
    public async Task Erasure_is_audited_with_counts_and_no_content()
    {
        var ctx = await WithAttributedEvidenceAsync();

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        var entry = ctx.AuditLog.Entries.Single(e => e.Action == SkillsEvidenceAuditAction.EvidenceErasedForSubject);
        var detail = JsonDocument.Parse(entry.DetailJson!).RootElement;

        Assert.Equal(1, detail.GetProperty("actorLinksDeleted").GetInt32());
        Assert.Equal(2, detail.GetProperty("evidenceRowsDeleted").GetInt32());
        // Nothing it just deleted is written back into a log that outlives it.
        Assert.DoesNotContain("sha1", entry.DetailJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("alex", entry.DetailJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Erasing_one_person_leaves_a_colleagues_evidence_alone()
    {
        var ctx = await WithAttributedEvidenceAsync();
        await ctx.ApproveAsync(ctx.ConnectionA, "7", ctx.Sarah, "unknown-contractor");
        Assert.Single(ctx.Repository.Evidence, e => e.StaffKey == ctx.Sarah.StaffKey);

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        Assert.Single(ctx.Repository.Evidence, e => e.StaffKey == ctx.Sarah.StaffKey);
        Assert.Single(ctx.Repository.ActorLinks);
    }

    [Fact]
    public async Task A_resync_after_erasure_does_not_resurrect_the_attribution()
    {
        var ctx = await WithAttributedEvidenceAsync();
        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);

        // The link is gone, so re-ingesting the same commits attributes
        // them to nobody. Erasing the links before the evidence is what
        // makes this true.
        var resolver = new EvidenceActorResolver(ctx.Repository, ctx.StaffRepository);
        var coordinator = new SyncRunCoordinator(
            new SyncRunGuard(),
            ctx.SyncRuns,
            Microsoft.Extensions.Options.Options.Create(new ProgrammeOpsOptions { SyncLeaseSeconds = 300 }),
            ctx.Time);
        var ingestion = new ProgrammePulse.Services.Integrations.GitHub.GitHubEvidenceIngestionService(
            ctx.Client, ctx.Repository, ctx.Protector, resolver, ctx.AuditLog, ctx.Continuity, ctx.SyncRuns,
            Microsoft.Extensions.Options.Options.Create(new ProgrammePulse.Services.Integrations.GitHub.SkillsEvidenceOptions()),
            coordinator,
            ctx.Time);

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));
        await ingestion.RunAsync(ctx.ConnectionA.ConnectionKey, EvidenceTestContext.TenantA, null);

        Assert.DoesNotContain(ctx.Repository.Evidence, e => e.StaffKey == ctx.Alex.StaffKey);
    }

    [Fact]
    public async Task Disconnecting_destroys_the_credential_and_withdraws_the_coverage_claim()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        await ctx.Repository.SetConnectionStatusAsync(
            ctx.ConnectionA.ConnectionKey, EvidenceConnectionStatus.Disconnected,
            EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime, clearCredential: true);
        await ctx.Repository.MarkCoverageOutOfScopeAsync(
            ctx.ConnectionA.ConnectionKey, [], EvidenceTestContext.TenantA, ctx.Time.Now.UtcDateTime);

        var connection = ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.ConnectionA.ConnectionKey);
        Assert.Null(connection.ProtectedCredentialJson);
        Assert.False(connection.IsUsable);

        // The other tenant's connection is untouched — disconnecting is
        // scoped to the connection, not to the provider.
        Assert.True(ctx.Repository.Connections.Single(c => c.ConnectionKey == ctx.ConnectionB.ConnectionKey).IsUsable);

        // Evidence is kept — it was true when collected — but a
        // disconnected source must not leave an apparently current claim.
        Assert.NotEmpty(ctx.Repository.Evidence);
        Assert.All(
            ctx.Repository.Coverage.Where(c => c.ConnectionKey == ctx.ConnectionA.ConnectionKey),
            c => Assert.Equal(EvidenceCoverageStatus.OutOfScope, c.Status));
    }

    [Fact]
    public async Task A_subject_with_no_evidence_exports_and_erases_exactly_as_before()
    {
        // Slice 1 behaviour must be unchanged for a tenant that has
        // connected nothing — the participant now covers both, and the
        // evidence half has to be a no-op when there is none.
        var ctx = new SkillsEvidenceTestContext();
        await ctx.AddSkillAsync("csharp", SkillsEvidenceTestContext.TenantA);
        await ctx.ValidatedAsync(ctx.Alex, "csharp", ProficiencyLevel.Working, ctx.Sarah, SkillsEvidenceTestContext.TenantA);

        var rows = await ctx.Participant.ExportAsync(ctx.Alex.StaffKey);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Skill assertions", r.Section));

        await ctx.Participant.EraseAsync(ctx.Alex.StaffKey, ctx.Time.Now.UtcDateTime);
        Assert.Empty(await ctx.Participant.ExportAsync(ctx.Alex.StaffKey));
    }
}
