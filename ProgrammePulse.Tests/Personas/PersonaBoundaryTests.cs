using ProgrammePulse.Tests.Integration;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// The role boundaries that matter most commercially, proved against the
/// real application: the tenant administrator's journey through a real TOTP
/// challenge, and the platform operator's exclusion from customer data.
///
/// These are the assertions a prospect's security reviewer asks for, so they
/// are deliberately written as journeys with an explicit allowed step and an
/// explicit refused step — a test that only ever asserts refusals can pass
/// against an application where nobody can do anything.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PersonaBoundaryTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "no persona boundary was exercised over real HTTP.";

    private bool Ready() => ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output);

    private async Task<PersonaSession> SignInAsync(PersonaDefinition persona)
    {
        await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync([persona]);
        return await PersonaSignIn.SignInAsync(factory, persona);
    }

/// <summary>
    /// Names the route and the status when it fails. "Expected Allowed,
    /// actual Other" on its own cannot be diagnosed after the fact - the
    /// interesting cases (500, 503, 404) all collapse into "Other".
    /// </summary>
    private static async Task AssertAllowedAsync(PersonaSession session, string path)
    {
        var response = await session.GetAsync(path);
        var outcome = PersonaSession.Outcome(response);

        Assert.True(outcome is AccessOutcome.Allowed,
            $"{session.PersonaName} was expected to reach {path} but observed {outcome} ({(int)response.StatusCode} {response.StatusCode}).");
    }

        private static async Task AssertDeniedAsync(PersonaSession session, string path)
    {
        var response = await session.GetAsync(path);
        var outcome = PersonaSession.Outcome(response);

        Assert.False(outcome is AccessOutcome.Malformed, $"{path} returned 400 — antiforgery, not authorization.");
        Assert.False(outcome is AccessOutcome.RateLimited, $"{path} returned 429 — the rate limiter, not authorization.");
        Assert.True(outcome is AccessOutcome.Denied,
            $"{session.PersonaName} reached {path} (observed {outcome}, {(int)response.StatusCode}) but must not.");
    }

    /// <summary>
    /// Emma holds Admin, so the password alone is not enough. This signs in
    /// through the real MFA challenge with a code computed from the seeded
    /// secret — proving the TOTP gate is real and that an Admin who clears
    /// it reaches tenant administration but not the platform console.
    /// </summary>
    [Fact]
    public async Task Tenant_admin_clears_a_real_totp_challenge_then_administers_only_her_own_tenant()
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);

        var roster = await session.GetAsync("/staffops/admin");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(roster));

        var audit = await session.GetAsync("/staffops/admin/audit");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(audit));

        // The whole point of the Admin / Platform Admin split: a customer's
        // own administrator cannot change their plan or lift a suspension.
        await AssertDeniedAsync(session, "/staffops/platform/tenants");
    }

    /// <summary>
    /// Actions now receive their tenant as a [CurrentTenant] Guid tenantId
    /// parameter. The name invites the obvious attack, ?tenantId=&lt;someone
    /// else's&gt;, so this proves over real HTTP, through real model binding,
    /// that the request can't choose the tenant. Emma asks for Meridian's
    /// roster and still gets Northstar's, with Meridian's person absent.
    /// </summary>
    [Fact]
    public async Task A_tenant_id_in_the_request_never_selects_another_tenant()
    {
        if (!Ready()) { return; }

        var meridianPerson = new PersonaDefinition(
            Key: "meridian-foil",
            FullName: "Meridian Foil Engineer",
            Email: "foil.engineer@meridian.test",
            MemberGroup: ProgrammePulse.Models.Staff.StaffRole.Staff,
            StaffKey: Guid.Parse("b2000000-0000-4000-8000-000000000001"),
            TenantKey: NorthstarPersonas.MeridianTenantKey,
            JobTitle: "Engineer",
            RequiresMfa: false);
        await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync([meridianPerson]);

        var session = await SignInAsync(NorthstarPersonas.TenantAdmin);
        var response = await session.GetAsync($"/staffops/admin?tenantId={NorthstarPersonas.MeridianTenantKey}");

        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(response));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(NorthstarPersonas.TenantAdmin.FullName, html, StringComparison.Ordinal);
        Assert.DoesNotContain(meridianPerson.FullName, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The section 3 determination of docs/persona-harness-build-gate.md,
    /// turned into a test: platform administration confers tenant lifecycle
    /// control, never tenant business data.
    ///
    /// The persona's staff profile deliberately sits *inside* Northstar's
    /// tenant, which is the adversarial configuration — so this proves the
    /// exclusion holds on role grounds alone, not merely because the data
    /// belongs to some other tenant.
    /// </summary>
    [Theory]
    [InlineData("/staffops")]
    [InlineData("/staffops/admin")]
    [InlineData("/staffops/admin/audit")]
    [InlineData("/staffops/reporting")]
    [InlineData("/staffops/reporting/cost")]
    [InlineData("/staffops/programme")]
    [InlineData("/staffops/contracts")]
    [InlineData("/staffops/branding")]
    public async Task Platform_admin_is_refused_every_tenant_business_page(string path)
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.PlatformAdmin);

        await AssertDeniedAsync(session, path);
    }

    /// <summary>
    /// The positive half of the same boundary — without it, the theory above
    /// would pass against a platform admin who could reach nothing at all.
    /// </summary>
    [Fact]
    public async Task Platform_admin_reaches_the_tenant_console()
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.PlatformAdmin);

        var console = await session.GetAsync("/staffops/platform/tenants");

        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(console));
    }

    /// <summary>
    /// Decision 2 of 19 September 2026, now live: the Board role reads the
    /// portfolio and delivery reporting, and writes nothing. This test was
    /// previously the inverse, recording defect D4 — it was inverted
    /// deliberately when the capability model landed.
    ///
    /// The write refusals are the substance. Without them "read-only" is an
    /// intention rather than a property, and Board would simply have been
    /// added to the old seniority union.
    /// </summary>
    [Fact]
    public async Task Board_reads_the_portfolio_and_reporting_but_cannot_change_anything()
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.Board);

        foreach (var readable in new[]
                 {
                     "/staffops/reporting/estimate-calibration",
                     "/staffops/reporting",
                     "/staffops/reporting/raid",
                     "/staffops/reporting/trend",
                     "/staffops/reporting/governance",
                     "/staffops/programme"
                 })
        {
            await AssertAllowedAsync(session, readable);
        }

        // Commercial and administrative areas remain closed.
        await AssertDeniedAsync(session, "/staffops/reporting/cost");
        await AssertDeniedAsync(session, "/staffops/admin");

        // Read-only means the write endpoints refuse a *properly formed*
        // request, token and all — otherwise this would only be proving that
        // antiforgery works.
        await AssertWriteDeniedAsync(session, "/staffops/reporting/raid", "/staffops/reporting/baseline/lock",
            new Dictionary<string, string> { ["workstreamKey"] = Guid.NewGuid().ToString() });
    }

    /// <summary>
    /// Group membership is not enough: a member whose staff profile is
    /// deactivated must lose access on the next request, not at the end of
    /// their cookie's life.
    ///
    /// This covers StaffAuthorizationService's active-profile requirement,
    /// which had no test at all - and was briefly lost during a working-tree
    /// incident without anything noticing. The login-time check in
    /// StaffAccountController is a different control and does not cover this:
    /// it only runs before the session exists.
    /// </summary>
    [Fact]
    public async Task A_deactivated_staff_profile_loses_access_immediately_despite_group_membership()
    {
        if (!Ready()) { return; }

        var persona = NorthstarPersonas.ProjectManager;
        var seeder = new NorthstarPersonaSeeder(factory.Services);
        var session = await SignInAsync(persona);

        // Baseline: signed in and entitled, so the refusal below cannot be
        // mistaken for the persona never having had access.
        await AssertAllowedAsync(session, "/staffops/reporting");

        try
        {
            await seeder.SetProfileActiveAsync(persona, false);

            // Same cookie, same groups, same session - only the profile changed.
            await AssertDeniedAsync(session, "/staffops/reporting");
            await AssertDeniedAsync(session, "/staffops");
        }
        finally
        {
            await seeder.SetProfileActiveAsync(persona, true);
        }

        // And access returns once reactivated, so the check is not a one-way latch.
        await AssertAllowedAsync(session, "/staffops/reporting");
    }

    /// <summary>
    /// Decision 1: the Analyst reads operational reporting without inheriting
    /// the delivery write access that making them a Team Lead would have
    /// carried. This is the persona the old model could not express at all.
    /// </summary>
    [Fact]
    public async Task Analyst_reads_operational_reporting_without_delivery_write_or_commercials()
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.Analyst);

        foreach (var readable in new[] { "/staffops/reporting", "/staffops/reporting/raid", "/staffops/programme" })
        {
            await AssertAllowedAsync(session, readable);
        }

        await AssertDeniedAsync(session, "/staffops/reporting/cost");
        await AssertDeniedAsync(session, "/staffops/contracts");
        await AssertDeniedAsync(session, "/staffops/admin");
        await AssertDeniedAsync(session, "/staffops/approvals");

        await AssertWriteDeniedAsync(session, "/staffops/reporting/raid", "/staffops/reporting/baseline/lock",
            new Dictionary<string, string> { ["workstreamKey"] = Guid.NewGuid().ToString() });
    }

    /// <summary>
    /// Posts a real, token-bearing request and requires it to be refused on
    /// authorization grounds. The token is lifted from a page the persona
    /// *can* reach, so a 400 here would mean the harness is broken rather
    /// than the endpoint being protected — which is why 400 fails.
    /// </summary>
    private static async Task AssertWriteDeniedAsync(
        PersonaSession session, string formPath, string postPath, Dictionary<string, string> fields)
    {
        var response = await session.PostWithTokenAsync(formPath, postPath, fields);

        Assert.NotNull(response);
        var outcome = PersonaSession.Outcome(response);

        Assert.False(outcome is AccessOutcome.Malformed,
            $"POST {postPath} returned 400 — antiforgery, not authorization.");
        Assert.True(outcome is AccessOutcome.Denied,
            $"{session.PersonaName} was able to POST {postPath} (observed {outcome}, {(int)response.StatusCode}) but is read-only.");
    }

    /// <summary>
    /// Defect D3, likewise: a leave approver currently reaches the reporting
    /// hub and its write actions because IsTeamLeadOrAboveAsync is a
    /// seniority union. Sarah (Team Lead) is the persona that *should* have
    /// this, and does — recorded here as the positive control for the
    /// capability split.
    /// </summary>
    [Fact]
    public async Task Project_manager_reaches_delivery_reporting_but_not_commercials()
    {
        if (!Ready()) { return; }

        var session = await SignInAsync(NorthstarPersonas.ProjectManager);

        var reporting = await session.GetAsync("/staffops/reporting");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(reporting));

        var raid = await session.GetAsync("/staffops/reporting/raid");
        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(raid));

        // Cost and rate data is Admin-only and structurally absent from the
        // Gold query a Team Lead can reach (see CLAUDE.md).
        await AssertDeniedAsync(session, "/staffops/reporting/cost");
        await AssertDeniedAsync(session, "/staffops/contracts");
        await AssertDeniedAsync(session, "/staffops/admin");
    }
}
