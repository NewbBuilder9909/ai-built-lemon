using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Web.Common.Security;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class TenantAdministratorProvisioningIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    [Fact]
    public async Task Provisioning_scopes_the_identity_and_rejects_duplicate_and_blocked_requests()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("administrator provisioning was not exercised against Umbraco and SQL.", output)) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenants = services.GetRequiredService<ITenantRepository>();
        var tenant = await tenants.CreateAsync(NewTenant());
        var service = services.GetRequiredService<ITenantAdministratorProvisioningService>();
        var input = NewInput();
        var result = await service.ProvisionAsync(tenant.TenantKey, input, 1);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var staff = services.GetRequiredService<IStaffRepository>();
        var profile = await staff.GetByStaffKeyAsync(result.StaffKey!.Value);
        Assert.Equal(tenant.TenantKey, profile!.TenantId);
        var members = services.GetRequiredService<MemberManager>();
        var member = await members.FindByEmailAsync(input.Email);
        Assert.NotNull(member);
        Assert.True(await members.IsInRoleAsync(member, StaffRole.Admin));
        Assert.False(await members.IsInRoleAsync(member, StaffRole.PlatformAdmin));
        Assert.False((await service.ProvisionAsync(tenant.TenantKey, input, 1)).Succeeded);
        Assert.Single(await staff.GetByTenantAsync(tenant.TenantKey));
        var audit = await services.GetRequiredService<IStaffAuditLogRepository>().GetForEntityAsync(tenant.TenantKey.ToString(), tenant.TenantKey);
        var entry = Assert.Single(audit, e => e.Action == "TenantAdministratorProvisioned");
        Assert.DoesNotContain(input.Password, entry.DetailJson!);
        Assert.DoesNotContain(input.Email, entry.DetailJson!);
        await tenants.UpdateAsync(tenant with { Status = TenantStatus.Suspended });
        var blockedInput = NewInput();
        Assert.False((await service.ProvisionAsync(tenant.TenantKey, blockedInput, 1)).Succeeded);
        Assert.Null(await members.FindByEmailAsync(blockedInput.Email));
        Assert.False((await service.ProvisionAsync(Guid.NewGuid(), NewInput(), 1)).Succeeded);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_member_and_profile_and_allows_retry()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("identity rollback was not exercised against Umbraco and SQL.", output)) return;
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenants = services.GetRequiredService<ITenantRepository>();
        var tenant = await tenants.CreateAsync(NewTenant());
        var input = NewInput();
        var staff = services.GetRequiredService<IStaffRepository>();
        var service = new TenantAdministratorProvisioningService(tenants, staff,
            services.GetRequiredService<IStaffOnboardingService>(), new FailingAudit(),
            services.GetRequiredService<IScopeProvider>(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvisionAsync(tenant.TenantKey, input, 1));
        Assert.Empty(await staff.GetByTenantAsync(tenant.TenantKey));
        Assert.Null(await services.GetRequiredService<MemberManager>().FindByEmailAsync(input.Email));
        var retry = await services.GetRequiredService<ITenantAdministratorProvisioningService>().ProvisionAsync(tenant.TenantKey, input, 1);
        Assert.True(retry.Succeeded, string.Join("; ", retry.Errors));
    }

    private static Tenant NewTenant() => new()
    {
        TenantKey = Guid.NewGuid(), Name = "Onboarding integration", ShortCode = "ob-" + Guid.NewGuid().ToString("N")[..20],
        IsActive = true, Status = TenantStatus.Trial, TrialEndsAtUtc = DateTime.UtcNow.AddDays(30),
        Plan = TenantPlan.Starter, CreatedAtUtc = DateTime.UtcNow
    };

    private static TenantAdministratorInput NewInput() => new()
    {
        FullName = "Onboarding Test", Email = $"onboarding-{Guid.NewGuid():N}@example.test", Password = "Test-Only!Password9876"
    };

    private sealed class FailingAudit : IStaffAuditLogRepository
    {
        public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
            => throw new InvalidOperationException("Injected audit failure");
        public Task<IReadOnlyList<StaffAuditLog>> GetRecentAsync(int take, Guid tenantId) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffAuditLog>> GetForEntityAsync(string entityId, Guid tenantId) => throw new NotSupportedException();
    }
}
