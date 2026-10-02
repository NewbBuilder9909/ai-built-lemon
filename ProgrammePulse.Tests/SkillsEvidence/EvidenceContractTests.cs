using ProgrammePulse.Services.Shared;
using System.Reflection;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// The rules that are supposed to be structural: no host outside the
/// allow-list, no score on a portfolio, no vendor type above the Bronze
/// boundary, and no path from evidence to a proficiency level.
/// </summary>
public class EvidenceContractTests
{
    // ---- Host allow-list ----

    [Theory]
    [InlineData("https://api.github.com", "https://api.github.com")]
    [InlineData("https://API.GitHub.com/", "https://api.github.com")]
    public void The_github_api_host_is_accepted_and_canonicalised(string candidate, string expected) =>
        Assert.Equal(expected, EvidenceHostPolicy.Canonicalize(candidate));

    [Theory]
    [InlineData("http://api.github.com")]                       // not HTTPS
    [InlineData("https://evil.test")]                           // not on the list
    [InlineData("https://api.github.com.evil.test")]            // suffix trick
    [InlineData("https://user:pass@api.github.com")]            // credentials in the URL
    [InlineData("https://api.github.com:8443")]                 // non-default port
    [InlineData("https://api.github.com/?redirect=evil.test")]  // query
    [InlineData("https://api.github.com/#x")]                   // fragment
    [InlineData("https://api.github.com/some/path")]            // unexpected path
    [InlineData("//api.github.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_refused_rather_than_defaulted(string? candidate) =>
        Assert.Null(EvidenceHostPolicy.Canonicalize(candidate));

    [Fact]
    public void An_enterprise_host_works_only_when_an_operator_has_listed_it()
    {
        const string candidate = "https://git.acme-internal.test";

        Assert.Null(EvidenceHostPolicy.Canonicalize(candidate));
        Assert.Equal(
            "https://git.acme-internal.test/api/v3",
            EvidenceHostPolicy.Canonicalize(candidate, ["git.acme-internal.test"]));
    }

    [Theory]
    [InlineData("acme/web", true)]
    [InlineData("acme-ltd/web.api", true)]
    [InlineData("acme/../etc", false)]
    [InlineData("acme/web/extra", false)]
    [InlineData("/acme/web", false)]
    [InlineData("acme/*", false)]
    [InlineData("acme", false)]
    [InlineData("", false)]
    public void A_repository_key_is_two_safe_path_segments(string candidate, bool expected) =>
        Assert.Equal(expected, EvidenceHostPolicy.IsValidRepositoryKey(candidate));

    [Fact]
    public void A_selection_may_not_smuggle_in_another_organisations_repository()
    {
        Assert.False(EvidenceHostPolicy.IsValidSelection(["acme/web", "rival/secrets"], "acme", out _));

        Assert.True(EvidenceHostPolicy.IsValidSelection(["acme/web", "ACME/API"], "acme", out var normalized));
        Assert.Equal(["acme/web", "acme/api"], normalized);
    }

    // ---- Provider neutrality ----

    /// <summary>
    /// The same rule SourceIndependenceTests applies to Silver and Gold,
    /// applied to this feature's namespaces. Written here rather than by
    /// extending that file because it is shared and another agent is
    /// active in the repository — the duplication is deliberate and small.
    ///
    /// Why it matters: the evidence contract is supposed to accept a
    /// GitLab or plain-git adapter later without Silver, the portfolio or
    /// the coverage view changing. That only stays true if nothing above
    /// the Bronze boundary can name a GitHub type.
    /// </summary>
    [Fact]
    public void The_evidence_contract_never_references_a_vendor_namespace()
    {
        string[] vendorNamespaces =
        [
            "ProgrammePulse.Services.Integrations.GitHub",
            "ProgrammePulse.Models.Integrations.GitHub"
        ];

        string[] neutralNamespaces =
        [
            "ProgrammePulse.Models.SkillsEvidence",
            "ProgrammePulse.Services.SkillsEvidence"
        ];

        var assembly = typeof(EngineeringEvidence).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes().Where(t => t.Namespace is not null && neutralNamespaces.Contains(t.Namespace)))
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                if (referenced.Namespace is { } ns && vendorNamespaces.Any(v => ns == v || ns.StartsWith(v + ".", StringComparison.Ordinal)))
                {
                    violations.Add($"{type.FullName} references {referenced.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "The evidence contract must stay provider-neutral so a second forge needs no changes above Bronze. Violations:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order()));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(All)) yield return field.FieldType;
        foreach (var property in type.GetProperties(All)) yield return property.PropertyType;

        foreach (var method in type.GetMethods(All))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }

        foreach (var constructor in type.GetConstructors(All))
        {
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        }
    }

    // ---- No score, no rank ----

    /// <summary>
    /// The design document rules out an automatic individual "value",
    /// "quality" or maturity rank. The way to guarantee a view cannot
    /// render one is to give it nothing to render, so this fails the
    /// build if a score-shaped member appears on the portfolio.
    /// </summary>
    [Fact]
    public void The_portfolio_exposes_no_score_rank_or_rating()
    {
        string[] forbidden = ["score", "rank", "rating", "grade", "percentile", "productivity", "value", "quality", "velocity"];

        var offenders = typeof(StaffEvidencePortfolio)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"StaffEvidencePortfolio gained a score-shaped member: {string.Join(", ", offenders)}. "
            + "Contribution evidence is for a human to read in context, not to rank people by.");
    }

    [Fact]
    public void Evidence_carries_no_field_that_could_hold_code_or_a_diff()
    {
        string[] forbidden = ["patch", "diff", "body", "content", "blob", "snippet"];

        var offenders = typeof(EngineeringEvidence)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"EngineeringEvidence gained a content-shaped member: {string.Join(", ", offenders)}. "
            + "Only metadata and links are stored — never source code, diffs or file contents.");
    }

    /// <summary>
    /// The forcing function for "no silent proficiency change": nothing in
    /// the evidence services may even mention the assertion service, so a
    /// future change cannot quietly wire activity into a skill level.
    /// </summary>
    [Fact]
    public void No_evidence_service_can_reach_the_assertion_lifecycle()
    {
        Type[] evidenceServices =
        [
            typeof(EvidencePortfolioQueryService),
            typeof(EvidenceActorResolver),
            typeof(EngineeringEvidenceRepository),
            typeof(ProgrammePulse.Services.Integrations.GitHub.GitHubEvidenceIngestionService),
            typeof(ProgrammePulse.Services.Integrations.GitHub.GitHubEvidenceMapper)
        ];

        foreach (var service in evidenceServices)
        {
            var dependencies = service.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToArray();

            Assert.DoesNotContain(typeof(ISkillAssertionService), dependencies);
            Assert.DoesNotContain(typeof(ISkillsEvidenceRepository), dependencies);
        }
    }

    [Fact]
    public async Task Ingesting_evidence_creates_no_skill_assertion()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(
            [Commit("sha1", Human("1", "alex"), paths: ["src/Billing.cs", "src/Invoice.cs"])], null, true));

        await ctx.RunAsync(ctx.ConnectionA);
        await ctx.ApproveAsync(ctx.ConnectionA, "1", ctx.Alex, "alex");

        // Language hints were derived, and no assertion was raised.
        Assert.Contains("csharp", ctx.Repository.Evidence.First(e => e.Role == EvidenceRole.CommitAuthor).LanguageHints);
        Assert.Empty(ctx.SkillsRepository.Assertions);
    }

    // ---- Portfolio honesty ----

    [Fact]
    public async Task An_empty_portfolio_with_incomplete_coverage_reads_as_inconclusive_not_as_zero()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([], null, false, "rate limited"));
        await ctx.RunAsync(ctx.ConnectionA);

        var portfolio = await ctx.Portfolio.BuildForStaffAsync(ctx.Alex.StaffKey, EvidenceTestContext.TenantA, PageRequest.First());

        Assert.Empty(portfolio.Evidence);
        Assert.True(portfolio.AbsenceIsInconclusive);
        Assert.False(portfolio.Coverage.IsComplete);
    }

    [Fact]
    public async Task Coverage_is_only_complete_through_the_weakest_repositorys_point()
    {
        var ctx = new EvidenceTestContext();
        ctx.Repository.Connections[0] = ctx.ConnectionA with { SelectedRepositories = [EvidenceTestContext.RepoA, "acme-ltd/api"] };

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([], null, true));
        ctx.Client.WithCommits("acme-ltd/api", new GitHubPage<GitHubCommit>([], null, false, "rate limited"));

        await ctx.RunAsync(ctx.Repository.Connections[0]);
        var coverage = await ctx.Portfolio.BuildCoverageAsync(EvidenceTestContext.TenantA);

        // One repository incomplete makes the whole claim incomplete —
        // a portfolio is only as complete as its least complete source.
        Assert.Null(coverage.CompleteThroughUtc);
        Assert.Equal(EvidenceCoverageStatus.Partial, coverage.WorstStatus);
    }

    [Fact]
    public async Task Unverified_replay_is_flagged_until_a_run_has_actually_succeeded()
    {
        var ctx = new EvidenceTestContext();

        var before = await ctx.Portfolio.BuildCoverageAsync(EvidenceTestContext.TenantA);
        Assert.True(before.IsUnverifiedReplay);

        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>([], null, true));
        await ctx.RunAsync(ctx.ConnectionA);

        var after = await ctx.Portfolio.BuildCoverageAsync(EvidenceTestContext.TenantA);
        Assert.False(after.IsUnverifiedReplay);
    }

    [Fact]
    public async Task A_portfolio_never_shows_another_tenants_evidence()
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoB, new GitHubPage<GitHubCommit>(
            [Commit("sha-b", Human("1", "alex"))], null, true));
        await ctx.RunAsync(ctx.ConnectionB);
        await ctx.ApproveAsync(ctx.ConnectionB, "1", ctx.Rhian, "alex");

        var portfolio = await ctx.Portfolio.BuildForStaffAsync(ctx.Rhian.StaffKey, EvidenceTestContext.TenantA, PageRequest.First());

        Assert.Empty(portfolio.Evidence);
        Assert.Empty(portfolio.MappedAccounts);
    }
}
