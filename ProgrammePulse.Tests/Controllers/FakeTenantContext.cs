using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Controllers;

public sealed class FakeTenantContext : ITenantContext
{
    public Guid? CurrentTenantId { get; set; }
    public Tenant? CurrentTenant { get; set; }
    public bool IsResolved { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }

    public Task EnsureResolvedAsync() => Task.CompletedTask;

    /// <summary>Convenience: a resolved, usable tenant on the given plan.</summary>
    public static FakeTenantContext ResolvedOn(string plan, Guid? tenantKey = null)
    {
        var key = tenantKey ?? Guid.NewGuid();
        return new FakeTenantContext
        {
            CurrentTenantId = key,
            IsResolved = true,
            CurrentTenant = new Tenant
            {
                TenantKey = key,
                Name = "Acme",
                ShortCode = "acme",
                IsActive = true,
                Status = TenantStatus.Active,
                Plan = plan,
                CreatedAtUtc = new DateTime(2026, 1, 1)
            }
        };
    }
}
