using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Tests.Personas;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The Jira and Tempo report over real HTTP and SQL: it renders, and its
/// Reporting tab appears, only for a tenant that has Jira or Tempo data. A
/// tenant without either gets no tab and a 404, so the page never shows an
/// empty report that reads as "all fine".
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class JiraTempoRenderIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the Jira and Tempo report was never rendered, or hidden, over real HTTP.";
    private const string ReportPath = "/staffops/reporting/jira-tempo";

    [Fact]
    public async Task The_report_and_its_tab_exist_only_for_a_tenant_with_jira_or_tempo_data()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var withTempo = await CreateTenantAsync();
        var without = await CreateTenantAsync();
        var issueId = Random.Shared.NextInt64(100_000, 999_999).ToString();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
            await repository.UpsertTimeEntryAsync(new TimeEntry
            {
                TimeEntryKey = Guid.NewGuid(), DurationHours = 3, WorkDate = today, IsBillable = false, BillabilityKnown = false,
                ExternalSource = "Tempo", ExternalId = $"render:{Guid.NewGuid():N}", SourceWorkItemExternalId = $"render-cloud:{issueId}",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }, withTempo);
            // Another source's time must not make the report appear.
            await repository.UpsertTimeEntryAsync(new TimeEntry
            {
                TimeEntryKey = Guid.NewGuid(), DurationHours = 5, WorkDate = today, ExternalSource = "FileImport",
                ExternalId = $"render:{Guid.NewGuid():N}", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            }, without);
        }

        var lead = await SignInAsync(withTempo);
        var page = await lead.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Jira isn&#x27;t connected", html);
        Assert.Contains(issueId, html);
        Assert.Contains($"href=\"{ReportPath}\"", await (await lead.GetAsync("/staffops/reporting")).Content.ReadAsStringAsync());

        var other = await SignInAsync(without);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(ReportPath)).StatusCode);
        Assert.DoesNotContain(ReportPath, await (await other.GetAsync("/staffops/reporting")).Content.ReadAsStringAsync());
    }

    private async Task<PersonaSession> SignInAsync(Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = NorthstarPersonas.ProjectManager with { StaffKey = key, TenantKey = tenant, Email = $"jira-tempo-{key:N}@northstar.test" };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return await PersonaSignIn.SignInAsync(factory, persona);
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Jira Tempo render test", ShortCode = "jt-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        // The report sits in the Reporting hub, a benched module; this tenant exists only for this test.
        await TestModules.SwitchOnForTestTenantAsync(factory.Services, key, Models.Tenancy.ProductModules.Reporting);
        return key;
    }
}
