using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The bench and the account actions over real HTTP, on tenants created for
/// each test: a new organisation starts with every module off, only its
/// Admin can switch one on, and the switch reaches that organisation alone.
/// A reset link really sets a password, works once, and a suspended person
/// can't sign in.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ModuleBenchIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string NotProduced = "the module bench and account actions were not exercised over HTTP.";
    private const string Modules = "/staffops/settings/modules";

    [Fact]
    public async Task A_new_organisation_starts_benched_and_only_its_admin_can_switch_a_module_on()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(NotProduced, output)) return;

        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        var admin = await PersonaAsync(NorthstarPersonas.TenantAdmin, tenant);
        var lead = await PersonaAsync(NorthstarPersonas.ProjectManager, tenant);
        var otherLead = await PersonaAsync(NorthstarPersonas.ProjectManager, other);

        var adminSession = await PersonaSignIn.SignInAsync(factory, admin);
        var benched = await adminSession.GetAsync("/staffops/reporting");
        Assert.Equal(HttpStatusCode.NotFound, benched.StatusCode);
        Assert.Contains("is switched off", await benched.Content.ReadAsStringAsync());

        var review = await (await adminSession.GetAsync("/staffops/programme/evidence-check")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("href=\"/staffops/reporting\"", review);
        Assert.Contains("href=\"/staffops/programme/evidence-check/reviews\"", review);

        var leadSession = await PersonaSignIn.SignInAsync(factory, lead);
        var refused = await leadSession.GetAsync(Modules);
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Contains("AccessDenied", refused.Headers.Location!.ToString());

        var switched = await adminSession.PostWithTokenAsync(Modules, $"{Modules}/{ProductModules.Reporting}",
            new Dictionary<string, string> { ["on"] = "true" });
        Assert.NotNull(switched);
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        Assert.Contains("message=", switched.Headers.Location!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await leadSession.GetAsync("/staffops/reporting")).StatusCode);
        Assert.Contains("href=\"/staffops/reporting\"",
            await (await leadSession.GetAsync("/staffops/programme/evidence-check")).Content.ReadAsStringAsync());

        var otherSession = await PersonaSignIn.SignInAsync(factory, otherLead);
        Assert.Equal(HttpStatusCode.NotFound, (await otherSession.GetAsync("/staffops/reporting")).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Equal([ProductModules.Reporting],
            await scope.ServiceProvider.GetRequiredService<IModuleSwitchRepository>().GetSwitchedOnAsync(tenant));
    }

    [Fact]
    public async Task A_reset_link_sets_a_new_password_once_and_a_suspended_person_cannot_sign_in()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(NotProduced, output)) return;

        var tenant = await CreateTenantAsync();
        var admin = await PersonaAsync(NorthstarPersonas.TenantAdmin, tenant);
        var developer = await PersonaAsync(NorthstarPersonas.Developer, tenant);
        var adminSession = await PersonaSignIn.SignInAsync(factory, admin);
        var detail = $"/staffops/admin/{developer.StaffKey}";

        var issued = await adminSession.PostWithTokenAsync(detail, $"{detail}/password-reset", new Dictionary<string, string>());
        Assert.NotNull(issued);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var link = WebUtility.HtmlDecode(Regex.Match(await issued.Content.ReadAsStringAsync(), "value=\"(https?://[^\"]+/staffops/account/reset\\?[^\"]+)\"").Groups[1].Value);
        Assert.NotEqual("", link);
        var resetPath = new Uri(link).PathAndQuery;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);

        const string newPassword = "Fresh-Passw0rd-2026!";
        var anonymous = new PersonaSession(PersonaSignIn.CreateClient(factory), "anonymous");
        var fields = new Dictionary<string, string> { ["m"] = query["m"]!, ["t"] = query["t"]!, ["password"] = newPassword, ["confirm"] = newPassword };
        var reset = await anonymous.PostWithTokenAsync(resetPath, "/staffops/account/reset", fields);
        Assert.Contains("Your password has been changed", await reset!.Content.ReadAsStringAsync());

        var reused = await anonymous.PostWithTokenAsync(resetPath, "/staffops/account/reset", fields);
        Assert.Contains("expired or has already been used", WebUtility.HtmlDecode(await reused!.Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.Redirect, (await SignInAsync(developer.Email, newPassword)).StatusCode);

        var suspended = await adminSession.PostWithTokenAsync(detail, $"{detail}/access", new Dictionary<string, string> { ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, suspended!.StatusCode);
        // Refused either as unapproved or as an inactive profile; either way the form comes back, no session.
        var refused = await SignInAsync(developer.Email, newPassword);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("ops-chip", await refused.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SignInAsync(string email, string password)
    {
        var session = new PersonaSession(PersonaSignIn.CreateClient(factory), email);
        return (await session.PostWithTokenAsync(PersonaSignIn.LoginPath, PersonaSignIn.LoginPath,
            new Dictionary<string, string> { ["email"] = email, ["password"] = password }))!;
    }

    private async Task<PersonaDefinition> PersonaAsync(PersonaDefinition basis, Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = basis with
        {
            StaffKey = key, TenantKey = tenant, Email = $"bench-{key:N}@northstar.test",
            FullName = $"{basis.FullName} {key.ToString("N")[..6]}"
        };
        // Plain SeedAsync: this test is about the default, so nothing is switched on.
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return persona;
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Module bench test", ShortCode = "mb-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }
}
