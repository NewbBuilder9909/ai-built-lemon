using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.StaffOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real SQL Server. Proves the two halves of the StaffOps_AuditLog
/// cross-tenant fix: the audit page's query returns only the caller's
/// tenant, and the migration's backfill attributes legacy rows to the tenant
/// that owns them — never wholesale to the default tenant, which would move
/// the disclosure rather than close it. Rows are dated in the far future so
/// "newest first" puts them on the first page whatever else the shared test
/// database holds.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffAuditLogTenantIsolationIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "StaffOps audit log tenant isolation and backfill attribution were not verified against real SQL Server.";

    [Fact]
    public async Task The_audit_page_query_returns_only_the_callers_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenantA = await CreateTenantAsync();
        var tenantB = await CreateTenantAsync();
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IStaffAuditLogRepository>();
        var when = DateTime.UtcNow.AddYears(40);
        var ownId = Guid.NewGuid().ToString();
        var foreignId = Guid.NewGuid().ToString();

        await audit.LogAsync("Staff", ownId, "Created", null, "{\"email\":\"a@tenant-a.example\"}", when, tenantA);
        await audit.LogAsync("Staff", foreignId, "Created", null, "{\"email\":\"b@tenant-b.example\"}", when.AddSeconds(1), tenantB);

        var seenByA = await audit.GetRecentAsync(50, tenantA);
        Assert.Contains(seenByA, e => e.EntityId == ownId && e.TenantId == tenantA);
        Assert.DoesNotContain(seenByA, e => e.EntityId == foreignId);
        Assert.All(seenByA, e => Assert.Equal(tenantA, e.TenantId));
    }

    [Fact]
    public async Task The_subject_export_query_returns_only_the_callers_rows_about_a_shared_key()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenantA = await CreateTenantAsync();
        var tenantB = await CreateTenantAsync();
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IStaffAuditLogRepository>();
        var subject = Guid.NewGuid().ToString();

        await audit.LogAsync("Staff", subject, "GdprExported", null, null, DateTime.UtcNow, tenantA);
        await audit.LogAsync("StaffRate", subject, "RateChanged", 7, "{\"costPerHour\":80}", DateTime.UtcNow, tenantB);

        var entry = Assert.Single(await audit.GetForEntityAsync(subject, tenantA));
        Assert.Equal("GdprExported", entry.Action);
        Assert.Equal(tenantA, entry.TenantId);
    }

    [Fact]
    public async Task Backfill_attributes_legacy_rows_to_their_owning_tenant_and_hides_unprovable_ones()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenantA = await CreateTenantAsync();
        var tenantB = await CreateTenantAsync();
        var memberB = Random.Shared.Next(900_000_000, int.MaxValue);
        var staffB = await CreateStaffAsync(tenantB, memberB);

        var staffRow = Guid.NewGuid().ToString();
        var rateRow = Guid.NewGuid().ToString();
        var tenantRow = tenantA.ToString();
        var actorRow = Guid.NewGuid().ToString();
        var orphanRow = Guid.NewGuid().ToString();
        var when = DateTime.UtcNow.AddYears(41);

        using (var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope())
        {
            // Legacy shape: written before the column existed, so tenantId is NULL.
            InsertLegacy(scope, "Staff", staffB.StaffKey.ToString(), "Created", null, when, staffRow);
            InsertLegacy(scope, "StaffRate", staffB.StaffKey.ToString(), "RateChanged", null, when, rateRow);
            InsertLegacy(scope, "Tenant", tenantRow, "TenantPlanChanged", null, when, tenantRow + "-log");
            InsertLegacy(scope, "Leave", Guid.NewGuid().ToString(), "Approved", memberB, when, actorRow);
            InsertLegacy(scope, "Staff", Guid.NewGuid().ToString(), "Created", null, when, orphanRow);

            AddStaffAuditLogTenantColumn.Backfill(scope.Database);

            Assert.Equal(tenantB, TenantOf(scope, staffRow));
            Assert.Equal(tenantB, TenantOf(scope, rateRow));
            Assert.Equal(tenantA, TenantOf(scope, tenantRow + "-log"));
            Assert.Equal(tenantB, TenantOf(scope, actorRow));

            // A staff key nobody owns: the shared test database holds many
            // tenants, so the single-tenant rule can't claim it — it must stay
            // hidden from everyone rather than land in the default tenant.
            Assert.Null(TenantOf(scope, orphanRow));
            scope.Complete();
        }

        using var readScope = factory.Services.CreateScope();
        var audit = readScope.ServiceProvider.GetRequiredService<IStaffAuditLogRepository>();
        var defaultTenantView = await audit.GetRecentAsync(200, Tenant.DefaultTenantKey);
        Assert.DoesNotContain(defaultTenantView, e => e.EntityId == staffB.StaffKey.ToString());
    }

    // detailJson carries a unique tag so each seeded row can be found again
    // independently of its entityId (two rows share a staff key on purpose).
    private static void InsertLegacy(IScope scope, string entityType, string entityId, string action, int? actor, DateTime when, string tag) =>
        scope.Database.Execute(
            "INSERT INTO StaffOps_AuditLog (logKey, entityType, entityId, action, actorMemberId, tenantId, detailJson, timestampUtc) " +
            "VALUES (@0, @1, @2, @3, @4, NULL, @5, @6)",
            Guid.NewGuid(), entityType, entityId, action, actor, "{\"tag\":\"" + tag + "\"}", when);

    private static Guid? TenantOf(IScope scope, string tag) =>
        scope.Database.ExecuteScalar<Guid?>(
            "SELECT tenantId FROM StaffOps_AuditLog WHERE detailJson = @0", "{\"tag\":\"" + tag + "\"}");

    private async Task<Guid> CreateTenantAsync()
    {
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantRepository>().CreateAsync(new Tenant
        {
            TenantKey = key, Name = "Audit isolation test", ShortCode = "al-" + key.ToString("N")[..20],
            IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        return key;
    }

    private async Task<StaffProfile> CreateStaffAsync(Guid tenant, int memberId)
    {
        using var scope = factory.Services.CreateScope();
        var now = DateTime.UtcNow;
        return await scope.ServiceProvider.GetRequiredService<IStaffRepository>().CreateAsync(new StaffProfile
        {
            StaffKey = Guid.NewGuid(), TenantId = tenant, MemberId = memberId,
            FullName = "Audit Isolation", Email = $"audit-{Guid.NewGuid():N}@example.test",
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
    }
}
