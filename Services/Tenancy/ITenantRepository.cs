using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

public interface ITenantRepository
{
    Task<Tenant?> GetByKeyAsync(Guid tenantKey);

    Task<IReadOnlyList<Tenant>> GetAllActiveAsync();

    /// <summary>Every tenant regardless of status — platform-admin console only.</summary>
    Task<IReadOnlyList<Tenant>> GetAllAsync();

    Task<Tenant> CreateAsync(Tenant tenant);

    /// <summary>Updates Name, Status, Plan, TrialEndsAtUtc, IsActive and UpdatedAtUtc for the row with the given TenantKey.</summary>
    Task<Tenant> UpdateAsync(Tenant tenant);
}
