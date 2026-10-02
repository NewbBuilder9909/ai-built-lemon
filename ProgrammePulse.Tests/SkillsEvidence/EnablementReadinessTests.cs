using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The pre-enablement check.
///
/// Its usefulness rests entirely on keeping three kinds of answer apart:
/// what was observed in this tenant's data, what is structural in the
/// product, and what is the customer's own word. Most of these tests are
/// about that separation rather than about any individual check —
/// collapsing them into one row of green ticks is how a readiness page
/// becomes reassurance theatre, which is the thing the design document's
/// whole posture is against.
/// </summary>
public class EnablementReadinessTests
{
    private static EnablementReadinessService ServiceFor(EvidenceTestContext ctx) =>
        new(ctx.Continuity, ctx.Repository, ctx.Time);

    [Fact]
    public async Task With_no_decision_recorded_the_report_blocks_on_the_customers_own_answers()
    {
        var ctx = new EvidenceTestContext();
        ctx.BlockCollection(EvidenceTestContext.TenantA);

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        Assert.False(report.NothingBlocking);
        Assert.All(report.Blocking, c => Assert.Equal(ReadinessKind.CustomerAttestation, c.Kind));
        Assert.Contains(report.Blocking, c => c.Area == "Lawful basis");
        Assert.Contains(report.Blocking, c => c.Area == "Worker notice");
        Assert.Contains(report.Blocking, c => c.Area == "DPIA");
        Assert.All(report.Blocking, c => Assert.NotNull(c.Remedy));
    }

    [Fact]
    public async Task With_a_valid_decision_nothing_blocks()
    {
        var ctx = new EvidenceTestContext();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        Assert.True(report.NothingBlocking);
    }

    [Fact]
    public async Task An_expired_decision_blocks_on_an_observed_check_not_an_attestation()
    {
        var ctx = new EvidenceTestContext();
        ctx.Time.Advance(TimeSpan.FromDays(400));

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        var currency = report.Blocking.Single(c => c.Area == "Decision currency");
        // Whether the date has passed is measurable; whether the basis is
        // sound is not.
        Assert.Equal(ReadinessKind.Observed, currency.Kind);
    }

    [Fact]
    public async Task The_lawful_basis_check_says_the_product_cannot_assess_it()
    {
        var ctx = new EvidenceTestContext();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);
        var basis = report.Checks.Single(c => c.Area == "Lawful basis");

        Assert.Equal(ReadinessKind.CustomerAttestation, basis.Kind);
        Assert.Contains("cannot assess", basis.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_partial_sync_needs_attention_without_blocking()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [], null, IsComplete: false, IncompleteReason: "rate limited"));
        await ctx.RunAsync(ctx.ConnectionA);

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        // Incomplete coverage is a reason to read the portfolio
        // carefully, not a reason to refuse to enable.
        var sync = report.NeedingAttention.Single(c => c.Area == "Sync completeness");
        Assert.Equal(ReadinessKind.Observed, sync.Kind);
        Assert.NotNull(sync.Remedy);
        Assert.True(report.NothingBlocking);
    }

    [Fact]
    public async Task Unidentified_accounts_need_attention_and_are_counted()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("9", "contractor"))], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        var mapping = report.NeedingAttention.Single(c => c.Area == "Identity mapping");
        Assert.Contains("1 accounts are unidentified", mapping.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checks_with_nothing_connected_read_as_not_applicable_rather_than_passing()
    {
        var ctx = new EvidenceTestContext();
        ctx.Repository.Connections.Clear();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        // "Nothing connected" is not the same as "credentials are fine".
        var credentials = report.Checks.Single(c => c.Area == "Credential protection");
        Assert.Equal(ReadinessResult.NotApplicable, credentials.Result);
    }

    [Fact]
    public async Task A_connection_that_lost_access_needs_attention()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.AccessLost.Add(EvidenceTestContext.RepoA);
        await ctx.RunAsync(ctx.ConnectionA);

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        Assert.Contains(report.NeedingAttention, c => c.Area == "Credential protection");
    }

    [Fact]
    public async Task Structural_checks_name_the_test_that_proves_them()
    {
        var ctx = new EvidenceTestContext();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);
        var structural = report.Checks.Where(c => c.Kind == ReadinessKind.Structural).ToList();

        Assert.NotEmpty(structural);
        // A structural claim with no proof named is an assertion, and the
        // whole point of the Kind split is not to make those.
        Assert.All(structural, c => Assert.True(
            c.Proof is not null
            && (c.Proof.Contains("Tests", StringComparison.Ordinal) || c.Proof.Contains("docs/", StringComparison.Ordinal)),
            $"'{c.Area}' claims a structural property without naming what proves it."));

        Assert.All(structural, c => Assert.Equal(ReadinessResult.Pass, c.Result));

        // The proof is for engineers; the page a data protection officer
        // reads never names a test, a table or a type.
        Assert.All(report.Checks, c => Assert.DoesNotMatch(@"Tests\b|_[A-Z][a-z]+|docs/|\bI[A-Z][a-z]+[A-Z]", c.Detail));
    }

    [Fact]
    public async Task The_report_covers_every_area_the_brief_names()
    {
        var ctx = new EvidenceTestContext();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);
        var areas = report.Checks.Select(c => c.Area).ToList();

        foreach (var required in new[]
        {
            "Tenant isolation", "Least privilege", "Credential protection", "Data minimisation",
            "Retention", "Disconnect and deletion", "Subject access and erasure", "Access audit",
            "Sync completeness", "Lawful basis", "Worker notice", "DPIA"
        })
        {
            Assert.Contains(required, areas);
        }
    }

    [Fact]
    public async Task Customer_attestations_are_listed_separately_so_their_weight_is_visible()
    {
        var ctx = new EvidenceTestContext();

        var report = await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA);

        Assert.Equal(3, report.CustomerAttestations.Count);
        Assert.All(report.CustomerAttestations, c => Assert.Equal(ReadinessKind.CustomerAttestation, c.Kind));
    }

    /// <summary>
    /// A percentage would invite somebody to enable at eighty per cent
    /// without reading which twenty were missing.
    /// </summary>
    [Fact]
    public void The_report_exposes_no_score()
    {
        string[] forbidden = ["score", "percent", "rating", "grade", "readinesslevel"];

        var offenders = typeof(EnablementReadinessReport)
            .GetProperties()
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"EnablementReadinessReport gained a score-shaped member: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public async Task The_report_never_reads_another_tenants_state()
    {
        var ctx = new EvidenceTestContext();
        ctx.BlockCollection(EvidenceTestContext.TenantB);

        // A is permitted and B is not, from the same repository.
        Assert.True((await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantA)).NothingBlocking);
        Assert.False((await ServiceFor(ctx).BuildAsync(EvidenceTestContext.TenantB)).NothingBlocking);
    }
}
