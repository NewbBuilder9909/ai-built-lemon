using System.Net;
using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// End-of-diagnostic deletion over real HTTP and real SQL Server. A
/// customer's imported data and recorded review are removed from every
/// table once the tenant is archived and a Platform Admin confirms; an
/// active customer can't be purged; another customer's data is untouched;
/// the deletion is recorded in the platform audit log, which survives it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DeliveryDataPurgeIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string PurgeNotProduced = "the end-of-diagnostic deletion was never exercised against a real database.";

    [Fact]
    public async Task An_archived_customers_delivery_data_is_deleted_from_every_table_and_nobody_elses_is()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(PurgeNotProduced, output)) return;

        var customer = await CreateTenantAsync("purge-me");
        var bystander = await CreateTenantAsync("keep-me");
        var operatorTenant = await CreateTenantAsync("operator");
        await SeedDiagnosticAsync(customer);
        await SeedDiagnosticAsync(bystander);

        using (var scope = factory.Services.CreateScope())
        {
            var counts = await scope.ServiceProvider.GetRequiredService<IDeliveryDataPurgeRepository>().CountAsync(customer.TenantKey);
            Assert.True(counts.Single(c => c.Table == "ProgrammeOps_WorkItem").Rows > 0);
            Assert.True(counts.Single(c => c.Table == "ProgrammeOps_EvidenceReviewFinding").Rows > 0);
            Assert.True(counts.Single(c => c.Table == "ProgrammeOps_RawConnectorPayload").Rows > 0);
        }

        var key = Guid.NewGuid();
        var platformAdmin = NorthstarPersonas.PlatformAdmin with
        {
            StaffKey = key, TenantKey = operatorTenant.TenantKey, Email = $"purge-operator-{key:N}@programmepulse.test"
        };
        await new NorthstarPersonaSeeder(factory.Services).SeedAsync([platformAdmin]);
        var session = await PersonaSignIn.SignInAsync(factory, platformAdmin);
        var purgePath = $"/staffops/platform/tenants/{customer.TenantKey}/purge";

        // Still active: previewable, not purgeable.
        var activePreview = await (await session.GetAsync(purgePath)).Content.ReadAsStringAsync();
        Assert.Contains("Suspend or archive it first", activePreview);
        Assert.DoesNotContain("Permanently delete delivery data", activePreview);
        var refused = await session.PostWithTokenAsync("/staffops/platform/tenants", purgePath,
            new Dictionary<string, string> { ["confirmShortCode"] = customer.ShortCode });
        Assert.Equal(HttpStatusCode.BadRequest, refused!.StatusCode);

        await ArchiveAsync(customer);

        var wrongCode = await session.PostWithTokenAsync(purgePath, purgePath, new Dictionary<string, string> { ["confirmShortCode"] = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode!.StatusCode);

        var purged = await session.PostWithTokenAsync(purgePath, purgePath, new Dictionary<string, string> { ["confirmShortCode"] = customer.ShortCode });
        Assert.Equal(HttpStatusCode.OK, purged!.StatusCode);
        Assert.Contains("Delivery data deleted", await purged.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDeliveryDataPurgeRepository>();
            Assert.All(await repository.CountAsync(customer.TenantKey), c => Assert.Equal(0, c.Rows));
            var kept = await repository.CountAsync(bystander.TenantKey);
            Assert.True(kept.Single(c => c.Table == "ProgrammeOps_WorkItem").Rows > 0);
            Assert.True(kept.Single(c => c.Table == "ProgrammeOps_EvidenceReviewFinding").Rows > 0);

            var audit = await scope.ServiceProvider.GetRequiredService<IStaffAuditLogRepository>().GetForEntityAsync(customer.TenantKey.ToString(), customer.TenantKey);
            Assert.Contains(audit, a => a.Action == "DeliveryDataPurged");
        }
    }

    private async Task SeedDiagnosticAsync(Tenant tenant)
    {
        using var scope = factory.Services.CreateScope();
        var import = scope.ServiceProvider.GetRequiredService<IDeliveryExportImportService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True((await import.ImportAsync(tenant.TenantKey, DeliveryExportKind.WorkItems, DeliveryExportSample.WorkItemsCsv(today), null)).Imported);
        Assert.True((await import.ImportAsync(tenant.TenantKey, DeliveryExportKind.TimeEntries, DeliveryExportSample.TimeEntriesCsv(today), null)).Imported);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IEvidenceReviewService>()
            .RecordAsync(tenant.TenantKey,
                (await scope.ServiceProvider.GetRequiredService<IEvidenceCheckService>().ResolveScopeAsync(tenant.TenantKey, new EvidenceScopeRequest())).Scope!,
                new EvidenceReviewActor(null, null)));
    }

    private async Task ArchiveAsync(Tenant tenant)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().UpdateAsync(tenant with
        {
            Status = TenantStatus.Archived, IsActive = false, UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<Tenant> CreateTenantAsync(string prefix)
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = $"Purge test {prefix}", ShortCode = $"{prefix}-{key.ToString("N")[..12]}",
            IsActive = true, Status = TenantStatus.Active, CreatedAtUtc = DateTime.UtcNow
        });
    }
}
