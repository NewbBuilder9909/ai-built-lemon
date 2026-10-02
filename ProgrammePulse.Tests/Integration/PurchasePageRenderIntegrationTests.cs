using ProgrammePulse.Tests.Personas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Renders the public /purchase page over real HTTP, anonymously.
///
/// Two reasons this is worth a real request rather than a unit test.
///
/// First, the same Razor blind spot <see cref="ContractsOverviewRenderIntegrationTests"/>
/// exists for: `RazorCompileOnBuild=false` means a `.cshtml` compiles on
/// first request, so a green build proves nothing about this page.
/// scripts/check-views.ps1 now type-checks it, but that cannot catch a
/// failure to bind CommercialOptions, a missing service registration, or the
/// page throwing on a null enquiry address.
///
/// Second, this is the only anonymous surface in the application and the
/// commercial front door. A 500 here is a lost enquiry from someone who will
/// not report it, and nothing else in the suite would notice. It is also the
/// one page where a regression could leak an admin-only figure to the public
/// internet, so that is asserted rather than assumed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PurchasePageRenderIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the public /purchase page was never compiled or rendered.";

    [Fact]
    public async Task The_public_purchase_page_renders_anonymously()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var client = PersonaSignIn.CreateClient(factory);

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        // No sign-in, no redirect to a login page: this must be reachable by
        // someone who has never heard of us.
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Fixed-scope weekly delivery evidence diagnostic", body, StringComparison.Ordinal);
        Assert.Contains("PMO or delivery lead", body, StringComparison.Ordinal);

        // One offer, priced once, under a name that doesn't collide with an
        // existing PPM product (docs/archive/cpo-readiness-review-2026-09-26.md, B4).
        Assert.Contains("One diagnostic, one fixed fee", body, StringComparison.Ordinal);
        Assert.Contains("£3,500 fixed", body, StringComparison.Ordinal);
        Assert.Contains("Delivery Evidence Check", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgrammePulse", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Create a trial tenant and first admin account", body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"trial-signup-form\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("you can sell", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_self_service_trial_and_plans_appear_only_when_switched_on()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var enabledFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Commercial:SelfServiceTrialEnabled"] = "true"
                })));
        using var client = enabledFactory.CreateClient();

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Create a trial tenant and first admin account", body, StringComparison.Ordinal);
        Assert.Contains("Plans for a connected workspace", body, StringComparison.Ordinal);
        Assert.Contains("One diagnostic, one fixed fee", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ROI_check_offers_no_break_even_fee_and_states_that_capacity_is_not_cash()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var client = PersonaSignIn.CreateClient(factory);

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        // The public-launch gate. The old calculator published a "break-even
        // diagnostic fee" that was simply hypothetical annual time value with
        // realisation, fees and adoption effort all omitted — a number a buyer
        // could mistake for a justified price. It must not come back.
        Assert.DoesNotContain("break-even", body, StringComparison.OrdinalIgnoreCase);

        // What replaced it: the buyer's own costs are inputs, and the result is
        // labelled capacity rather than money.
        Assert.Contains("roi-realisation", body, StringComparison.Ordinal);
        Assert.Contains("roi-adoption", body, StringComparison.Ordinal);
        Assert.Contains("Year-one net position", body, StringComparison.Ordinal);
        Assert.Contains("capacity, not cash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unconfigured_enquiry_address_is_stated_rather_than_rendered_as_an_empty_mailto()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var client = PersonaSignIn.CreateClient(factory);

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        // Commercial:EnquiryEmail is pinned empty by the factory, so this is
        // the committed-appsettings state and not whatever the developer has in
        // user secrets. The failure mode being guarded is silent:
        // `href="mailto:"` looks like a working button, opens an email client
        // with no recipient, and loses the enquiry with no error anywhere.
        Assert.DoesNotContain("href=\"mailto:\"", body, StringComparison.Ordinal);
        Assert.Contains("not configured", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_configured_enquiry_address_becomes_a_real_mailto_and_drops_the_warning()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        // The other half of the branch. Configuration is supplied here rather
        // than read from the machine, so this asserts the behaviour of the code
        // rather than the state of somebody's user secrets.
        const string configured = "diagnostic@example.com";

        using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Commercial:EnquiryEmail"] = configured
                })));

        using var client = configuredFactory.CreateClient();

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"mailto:{configured}", body, StringComparison.Ordinal);
        Assert.Contains($"data-enquiry-email=\"{configured}\"", body, StringComparison.Ordinal);

        // The warning must disappear, or a working route still reads as broken.
        Assert.DoesNotContain("not configured", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_public_page_exposes_no_admin_only_commercial_figures()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var client = PersonaSignIn.CreateClient(factory);

        using var response = await client.GetAsync("/purchase");
        var body = await response.Content.ReadAsStringAsync();

        // Anchor first. Every other assertion here is a DoesNotContain, and
        // those all pass against an empty body or an error page — which would
        // make this test report success while proving nothing.
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Fixed-scope weekly delivery evidence diagnostic", body, StringComparison.Ordinal);

        // Cost/rate isolation is structural everywhere else in this codebase
        // (see CLAUDE.md). This page is anonymous, so the same rule is pinned
        // here explicitly: no staff cost rate and no contract commercial figure
        // may reach it. The worked example on the page is synthetic and must
        // stay that way.
        foreach (var leak in new[] { "StaffRate", "CumulativeCost", "MarginBasis", "cost rate" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
