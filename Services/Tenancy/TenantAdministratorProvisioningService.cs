using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Tenancy;

public interface ITenantAdministratorProvisioningService
{
    Task<TenantOnboardingViewModel?> GetWorkspaceAsync(Guid tenantKey);
    Task<StaffOnboardingResult> ProvisionAsync(Guid tenantKey, TenantAdministratorInput input, int actorMemberId);
}

/// <summary>Platform-only orchestration; never moves an existing identity between tenants.</summary>
public sealed class TenantAdministratorProvisioningService(
    ITenantRepository tenants,
    IStaffRepository staff,
    IStaffOnboardingService onboarding,
    IStaffAuditLogRepository audit,
    IScopeProvider scopes,
    TimeProvider clock) : ITenantAdministratorProvisioningService
{
    public async Task<TenantOnboardingViewModel?> GetWorkspaceAsync(Guid tenantKey)
    {
        var tenant = await tenants.GetByKeyAsync(tenantKey);
        if (tenant is null) return null;
        var access = TenantAccessPolicy.Evaluate(tenant, clock.GetUtcNow().UtcDateTime);
        var roster = await staff.GetByTenantAsync(tenantKey);
        return new TenantOnboardingViewModel(tenant, access.Allowed, access.Reason, roster.Count(s => s.IsActive), []);
    }

    public async Task<StaffOnboardingResult> ProvisionAsync(Guid tenantKey, TenantAdministratorInput input, int actorMemberId)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            return StaffOnboardingResult.Failure(errors.Select(e => e.ErrorMessage!).ToArray());

        // Enclose nested Umbraco repository/member writes and audit in one scope.
        // No completion on a failed Identity result or an exception.
        using var scope = scopes.CreateScope();
        var tenant = await tenants.GetByKeyAsync(tenantKey);
        if (tenant is null)
            return StaffOnboardingResult.Failure("Organisation not found.");
        var access = TenantAccessPolicy.Evaluate(tenant, clock.GetUtcNow().UtcDateTime);
        if (!access.Allowed)
            return StaffOnboardingResult.Failure(access.Reason!);

        var result = await onboarding.CreateStaffAsync(new StaffOnboardingRequest(
            input.FullName, input.Email, input.Password, StaffRole.Admin,
            null, null, null, 37.5m), tenantKey);
        if (!result.Succeeded) return result;

        await audit.LogAsync("Tenant", tenantKey.ToString(), "TenantAdministratorProvisioned", actorMemberId,
            JsonSerializer.Serialize(new { staffKey = result.StaffKey, role = StaffRole.Admin }), clock.GetUtcNow().UtcDateTime, tenantKey);
        scope.Complete();
        return result;
    }
}
