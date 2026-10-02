using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Tenancy;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The diagnostic's whole path over real HTTP and real SQL Server: a
/// customer's export goes in through the file import, and a delivery lead
/// signed in as that tenant sees the Evidence Check render its findings and
/// downloads the exception register. A second tenant sees none of it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvidenceCheckRenderIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the Evidence Check was never rendered from imported data over real HTTP.";

    [Fact]
    public async Task Imported_data_renders_as_findings_and_exports_as_a_safe_register_only_for_its_own_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenant = await CreateTenantAsync();
        var other = await CreateTenantAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var lateId = $"LATE-{marker}";
        var oddId = $"ODD-{marker}";

        using (var scope = factory.Services.CreateScope())
        {
            var import = scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>();
            var items = await import.ImportAsync(tenant, DeliveryExportKind.WorkItems,
                "Project,WorkItemId,Title,Status,DueDate,EstimatedHours\n" +
                $"Rollout,{lateId},\"=HYPERLINK(\"\"http://evil.example\"\")\",In progress,2020-01-31,4\n" +
                $"Rollout,{oddId},Waiting item,Waiting on client,,\n", null);
            Assert.True(items.Imported, items.Summary);
            var time = await import.ImportAsync(tenant, DeliveryExportKind.TimeEntries,
                // Dated inside the default seven-day period the page checks.
                $"WorkItemId,Date,Hours\n{lateId},{DateTime.UtcNow.AddDays(-1):yyyy-MM-dd},6\n,{DateTime.UtcNow.AddDays(-2):yyyy-MM-dd},2\n", null);
            Assert.True(time.Imported, time.Summary);
        }

        var lead = await PersonaAsync(NorthstarPersonas.ProjectManager, tenant);
        var session = await PersonaSignIn.SignInAsync(factory, lead);

        var page = await session.GetAsync("/staffops/programme/evidence-check");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Not decision-ready", html);
        Assert.Contains("Status can&#x27;t be read as a lifecycle stage", html);
        Assert.Contains("Open work past its due date", html);
        Assert.Contains("Work that has already used more than its estimate", html);
        Assert.Contains("Recorded time not linked to any work item", html);
        Assert.Contains(oddId, html);
        Assert.DoesNotContain("<a href=\"http://evil.example", html);

        var export = await session.GetAsync("/staffops/programme/evidence-check/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains(lateId, csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
        Assert.DoesNotContain("\"=HYPERLINK", csv);

        var outsider = await PersonaSignIn.SignInAsync(factory, await PersonaAsync(NorthstarPersonas.ProjectManager, other));
        var outsiderHtml = await (await outsider.GetAsync("/staffops/programme/evidence-check")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(lateId, outsiderHtml);
        Assert.DoesNotContain(lateId, await (await outsider.GetAsync("/staffops/programme/evidence-check/export")).Content.ReadAsStringAsync());
    }

    private async Task<PersonaDefinition> PersonaAsync(PersonaDefinition basis, Guid tenant)
    {
        var key = Guid.NewGuid();
        var persona = basis with { StaffKey = key, TenantKey = tenant, Email = $"evidence-{key:N}@northstar.test" };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([persona]);
        return persona;
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Evidence check test", ShortCode = "ec-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }
}
