using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Until 24 September 2026 the Admin audit page read StaffOps_AuditLog
/// unscoped (the latest 100 rows platform-wide), so any tenant's Admin saw
/// other organisations' entries: new staff with their email address,
/// cost-rate changes, GDPR erasures, and the platform operator's tenant
/// lifecycle actions.
///
/// Every row now carries the tenantId it was written under
/// (AddStaffAuditLogTenantColumn), and the read filters on it. This pins
/// that across all three entity kinds the page shows — Staff, StaffRate and
/// Tenant rows — against real SQL Server. Each run uses fresh tenant and
/// staff keys, so it shares the persistent test database with every other run.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffAuditTenantIsolationIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "no tenant scoping of the staff audit trail was exercised against real SQL Server.";

    [Fact]
    public async Task A_tenant_sees_audit_rows_for_its_own_people_and_itself_but_never_another_tenants()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var scope = factory.Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var audit = scope.ServiceProvider.GetRequiredService<IStaffAuditLogRepository>();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var personA = await SeedStaffAsync(staff, tenantA);
        var personB = await SeedStaffAsync(staff, tenantB);
        var now = DateTime.UtcNow;

        await audit.LogAsync("Staff", personA.StaffKey.ToString(), "Created", personA.MemberId, "{\"email\":\"a@a.test\"}", now, tenantA);
        await audit.LogAsync("StaffRate", personA.StaffKey.ToString(), "RateChanged", personA.MemberId, "{\"costPerHour\":50}", now, tenantA);
        await audit.LogAsync("Staff", personB.StaffKey.ToString(), "Created", personB.MemberId, "{\"email\":\"b@b.test\"}", now, tenantB);
        await audit.LogAsync("StaffRate", personB.StaffKey.ToString(), "RateChanged", personB.MemberId, "{\"costPerHour\":95}", now, tenantB);
        await audit.LogAsync("Tenant", tenantA.ToString(), "TenantPlanChanged", null, null, now, tenantA);
        await audit.LogAsync("Tenant", tenantB.ToString(), "TenantPlanChanged", null, null, now, tenantB);

        var visibleToA = await audit.GetRecentAsync(100, tenantA);

        Assert.Contains(visibleToA, e => e.EntityId == personA.StaffKey.ToString() && e.Action == "Created");
        Assert.Contains(visibleToA, e => e.EntityId == personA.StaffKey.ToString() && e.Action == "RateChanged");
        Assert.Contains(visibleToA, e => e.EntityType == "Tenant" && e.EntityId == tenantA.ToString());

        Assert.DoesNotContain(visibleToA, e => e.EntityId == personB.StaffKey.ToString());
        Assert.DoesNotContain(visibleToA, e => e.EntityId == tenantB.ToString());
        Assert.DoesNotContain(visibleToA, e => e.DetailJson != null && e.DetailJson.Contains("b@b.test"));
    }

    private static Task<StaffProfile> SeedStaffAsync(IStaffRepository staff, Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var key = Guid.NewGuid();
        return staff.CreateAsync(new StaffProfile
        {
            StaffKey = key,
            // Negative, unique and never a real Umbraco member id.
            MemberId = -Math.Abs(key.GetHashCode() % 900_000_000) - 1_000_000,
            FullName = $"Audit Isolation {key:N}",
            Email = $"audit-isolation-{key:N}@example.test",
            TenantId = tenantId,
            DefaultWorkHoursPerWeek = 37.5m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }
}
