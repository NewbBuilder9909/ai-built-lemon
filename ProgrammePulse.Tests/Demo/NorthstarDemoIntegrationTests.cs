using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Tests.Integration;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Demo;

/// <summary>
/// Boots the app on its own database, loads the Northstar demo once, and
/// holds it for the tests in <see cref="NorthstarDemoCollection"/>.
///
/// The database is <c>PP_DEMO_SQL_CONNECTION</c> when set — point it at
/// your development database to load the demo there (docs/demo-data.md) —
/// otherwise the integration server with the database name
/// <c>UmbracoBase_Demo</c>. Never the shared integration database: 25 staff
/// in the default tenant would change what every other suite sees.
/// </summary>
public sealed class NorthstarDemoFixture : IAsyncLifetime
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("PP_DEMO_SQL_CONNECTION")
        ?? ProgrammePulseWebApplicationFactory.ConnectionStringFor("UmbracoBase_Demo");

    public ProgrammePulseWebApplicationFactory? Factory { get; private set; }
    public NorthstarDemoSeedResult? Result { get; private set; }

    private readonly Dictionary<string, PersonaSession> _sessions = [];

    /// <summary>
    /// One sign-in per persona for the whole collection: a TOTP code is
    /// single-use within its 30-second step, so a second Admin sign-in in
    /// the same step is refused — correctly — by the replay check.
    /// </summary>
    public async Task<PersonaSession> SessionAsync(PersonaDefinition persona)
    {
        if (!_sessions.TryGetValue(persona.Key, out var session))
        {
            session = await PersonaSignIn.SignInAsync(Factory!, NorthstarDemoSeeder.Personas.Single(p => p.Key == persona.Key));
            _sessions[persona.Key] = session;
        }
        return session;
    }

    public async Task InitializeAsync()
    {
        if (!ProgrammePulseWebApplicationFactory.LocalDbAvailable)
            return;

        Factory = new ProgrammePulseWebApplicationFactory(ConnectionString);
        _ = Factory.Services;
        Result = await new NorthstarDemoSeeder(Factory.Services).SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
    }
}

/// <summary>
/// Its own collection, not the shared integration one: collections that
/// disable parallelisation run one at a time, so this host never boots
/// alongside the shared one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NorthstarDemoCollection : ICollectionFixture<NorthstarDemoFixture>
{
    public const string Name = "Northstar demo";
}

/// <summary>
/// The demo walked as a buyer would see it, one value case per test, over
/// real HTTP as the persona who would be looking. Each asserts on what the
/// page says rather than that it merely returned 200.
/// </summary>
[Collection(NorthstarDemoCollection.Name)]
public sealed class NorthstarDemoIntegrationTests(NorthstarDemoFixture demo, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the Northstar demo was never loaded or walked over real HTTP.";

    private async Task<PersonaSession?> SignInAsync(PersonaDefinition persona)
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output))
            return null;
        return await demo.SessionAsync(persona);
    }

    /// <summary>Benched modules start off on the demo, as they do for a customer; a test of a module's page switches it on for itself.</summary>
    private Task<IAsyncDisposable> ModulesOnAsync(params string[] modules) =>
        TestModules.OnAsync(demo.Factory!.Services, NorthstarDemoSeeder.Tenant, modules);

    private static async Task<string> PageAsync(PersonaSession session, string path)
    {
        var response = await session.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} returned {(int)response.StatusCode}");
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_whole_agency_loads_through_the_real_services()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = demo.Factory!.Services.CreateScope();
        var staff = await scope.ServiceProvider.GetRequiredService<IStaffRepository>().GetByTenantAsync(NorthstarDemoSeeder.Tenant);
        var programme = scope.ServiceProvider.GetRequiredService<IProgrammeReadRepository>();

        Assert.True(staff.Count >= 25, $"{staff.Count} staff");
        Assert.Equal(5, (await programme.GetProgrammesAsync(NorthstarDemoSeeder.Tenant)).Count);
        Assert.Equal(NorthstarDemoDataset.WorkItems.Count, (await programme.GetWorkItemsAsync(NorthstarDemoSeeder.Tenant)).Count);
        Assert.True((await programme.GetPlannedAllocationsAsync(NorthstarDemoSeeder.Tenant)).Count >= 300);
        Assert.Equal(4, demo.Result!.Contracts);
    }

    [Fact]
    public async Task A_project_manager_sees_the_portfolio_with_the_overdue_milestone_and_none_of_Meridians_data()
    {
        var session = await SignInAsync(NorthstarPersonas.ProjectManager);
        if (session is null) return;

        var html = await PageAsync(session, "/staffops/programme");

        Assert.Contains(NorthstarDemoDataset.Harbour, html);
        Assert.Contains(NorthstarDemoDataset.Kestrel, html);
        Assert.Contains("HL7 feeds sign-off", html);
        // Meridian imported the sales sample; none of it may appear here.
        Assert.DoesNotContain("Website rebuild", html);
        Assert.DoesNotContain("Mobile app phase 2", html);

        // Owen is over-booked: a Delivery load finding, shown once that module is on.
        await using (await ModulesOnAsync(ProductModules.DeliveryLoad))
        {
            Assert.Contains("Owen Griffiths", await PageAsync(session, "/staffops/programme"));
        }
    }

    [Fact]
    public async Task The_evidence_check_says_the_portfolio_needs_caveats_and_names_the_retainer()
    {
        var session = await SignInAsync(NorthstarPersonas.ProjectManager);
        if (session is null) return;

        var html = await PageAsync(session, "/staffops/programme/evidence-check");

        Assert.Contains("Use with caveats", html);
        Assert.Contains("Managed service", html);
        Assert.Contains("Waiting on supplier", html);
        Assert.Contains("CTA-313", html);
    }

    [Fact]
    public async Task An_admin_finds_the_unmatched_contractor_on_the_identity_queue()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;

        var html = await PageAsync(session, "/staffops/programme/identities");

        Assert.Contains("sam.okoro@contractor.example", html);
    }

    [Fact]
    public async Task An_admin_sees_every_contract_on_the_margin_overview()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;

        await using var modules = await ModulesOnAsync(ProductModules.Contracts);
        var html = await PageAsync(session, "/staffops/contracts/overview");

        foreach (var contract in NorthstarDemoDataset.Contracts.Where(c => c.Status == Models.ContractOps.ContractStatus.Active))
            Assert.Contains(contract.Customer, html);
    }

    [Fact]
    public async Task Skills_coverage_flags_HL7_and_Azure_as_single_maintainer()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;

        await using var modules = await ModulesOnAsync(ProductModules.Skills);
        var html = await PageAsync(session, "/staffops/skills/coverage");

        Assert.Contains("HL7 and FHIR integration", html);
        Assert.Contains("Azure infrastructure", html);
        Assert.Contains("One reviewed person, no reviewed backup", html);
    }

    [Fact]
    public async Task Pending_leave_waits_on_the_approvals_page()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;

        await using var modules = await ModulesOnAsync(ProductModules.SelfService);
        var html = await PageAsync(session, "/staffops/approvals");

        Assert.Contains("Ben Carter", html);
        Assert.Contains("Lucy Hughes", html);
    }

    [Fact]
    public async Task A_developer_sees_their_own_work_and_nobody_elses_cost()
    {
        var session = await SignInAsync(NorthstarPersonas.Developer);
        if (session is null) return;

        await using var modules = await ModulesOnAsync(ProductModules.SelfService);
        var html = await PageAsync(session, "/staffops/my-work");

        Assert.Contains("Proxy access for carers", html);
        Assert.DoesNotContain("62.00", html);
    }

    [Fact]
    public async Task Every_hour_in_the_window_is_costed_because_rates_predate_it()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;

        await using var modules = await ModulesOnAsync(ProductModules.Reporting);
        var html = await PageAsync(session, "/staffops/reporting/cost");

        Assert.Contains("Owen Griffiths", html);
        Assert.DoesNotContain("No priced time entries in this period.", html);
    }

    [Fact]
    public async Task Estimates_were_captured_before_the_work_and_reviewed_after_it()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = demo.Factory!.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<EstimateCalibrationService>()
            .BuildReportAsync(NorthstarDemoSeeder.Tenant, includePeople: true);

        var comparable = NorthstarDemoDataset.Estimates.Count(e => e.Review == NorthstarDemoDataset.DemoEstimateReview.Comparable);
        Assert.Equal(comparable, report.SampleCount);
        Assert.Equal(1, report.ExcludedCount);
        // Health's estimates run light, so the portfolio median is above one.
        Assert.True(report.PortfolioMedianRatio > 1m, $"median {report.PortfolioMedianRatio}");
    }

    [Fact]
    public async Task Alerts_and_a_truthful_trend_snapshot_are_there_without_anyone_running_a_sync()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = demo.Factory!.Services.CreateScope();
        var alerts = await scope.ServiceProvider.GetRequiredService<IAlertDetectionService>().CountOpenAlertsAsync(NorthstarDemoSeeder.Tenant);
        var trend = await scope.ServiceProvider.GetRequiredService<IReportingSnapshotService>().GetHistoryAsync(NorthstarDemoSeeder.Tenant);

        Assert.True(alerts > 0);
        // One snapshot, captured the morning after its period ended: a backdated
        // snapshot would file today's figures under an earlier period.
        var snapshot = Assert.Single(trend);
        Assert.Null(ReportingSnapshotService.CapturedLateByDays(snapshot));
    }

    [Fact]
    public async Task This_weeks_review_is_recorded_with_owners_and_a_stated_caveat()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = demo.Factory!.Services.CreateScope();
        var review = Assert.Single(await scope.ServiceProvider.GetRequiredService<IEvidenceReviewService>().GetHistoryAsync(NorthstarDemoSeeder.Tenant));

        var fixes = review.Findings.Where(f => f.Disposition == FindingDisposition.FixAtSource).ToList();
        Assert.NotEmpty(fixes);
        Assert.All(fixes, f => Assert.True(f.OwnerStaffKey is not null && f.TargetDate is not null));
        Assert.Contains(review.Findings, f => f.Disposition == FindingDisposition.AcceptAndState && f.Note is not null);
        // Most findings still wait on a decision, which is what a real first review looks like.
        Assert.Contains(review.Findings, f => f.Disposition == FindingDisposition.Open);
    }

    [Fact]
    public async Task Every_delivery_page_renders_for_the_admin_with_the_demo_loaded()
    {
        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        if (session is null) return;
        using var scope = demo.Factory!.Services.CreateScope();
        var review = (await scope.ServiceProvider.GetRequiredService<IEvidenceReviewService>().GetHistoryAsync(NorthstarDemoSeeder.Tenant))[0];

        await using var modules = await ModulesOnAsync();
        string[] pages =
        [
            "/staffops/programme/load", "/staffops/reporting", "/staffops/reporting/cost", "/staffops/reporting/raid",
            "/staffops/reporting/governance", "/staffops/reporting/estimate-calibration", "/staffops/contracts",
            "/staffops/skills/continuity", "/staffops/admin", "/staffops/programme/evidence-check",
            "/staffops/programme/evidence-check/reviews", $"/staffops/programme/evidence-check/reviews/{review.ReviewKey}",
            $"/staffops/programme/evidence-check/reviews/{review.ReviewKey}/pack"
        ];
        foreach (var path in pages)
            await PageAsync(session, path);
    }
    // Design and PMO data review, 30 Sep 2026: the verdict is drawn with the
    // projects behind it, the pack opens on the same answer, the product is in
    // the menu, and "overdue" is one number on every page.
    [Fact]
    public async Task The_verdict_is_drawn_with_its_projects_and_overdue_is_one_number_on_every_page()
    {
        var session = await SignInAsync(NorthstarPersonas.ProjectManager);
        if (session is null) return;

        var check = await PageAsync(session, "/staffops/programme/evidence-check");
        var overview = await PageAsync(session, "/staffops/programme");

        Assert.Contains("ops-band__segment--caveats ops-band__segment--current", check);
        Assert.Matches(@"\d+ of \d+ projects are not decision-ready", check);
        Assert.Contains(">Review</a>", overview);
        Assert.DoesNotContain("stage InProgress", check);

        var overviewOverdue = Regex.Match(overview, @"ops-attention__count"">(\d+)</span>\s*<span class=""ops-attention__body"">\s*<span class=""ops-attention__title"">Open work past its due date<").Groups[1].Value;
        var checkOverdue = Regex.Match(check, @"ops-finding__title"">Open work past its due date</span>[\s\S]*?ops-finding__figure"">(\d+) of").Groups[1].Value;
        Assert.NotEqual("", overviewOverdue);
        Assert.Equal(overviewOverdue, checkOverdue);

        using var scope = demo.Factory!.Services.CreateScope();
        var review = (await scope.ServiceProvider.GetRequiredService<IEvidenceReviewService>().GetHistoryAsync(NorthstarDemoSeeder.Tenant))[0];
        var pack = await PageAsync(session, $"/staffops/programme/evidence-check/reviews/{review.ReviewKey}/pack");
        Assert.Contains("ops-band__segment--current", pack);
        Assert.Matches(@"\d+ of \d+ projects are not decision-ready", pack);
        // Page one is the answer; the register starts on page two.
        Assert.True(pack.IndexOf("What was found", StringComparison.Ordinal) < pack.IndexOf("class=\"register\"", StringComparison.Ordinal));
    }
}
