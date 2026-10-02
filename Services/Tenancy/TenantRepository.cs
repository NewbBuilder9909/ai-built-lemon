using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Tenancy;

public sealed class TenantRepository(IScopeProvider scopeProvider) : ITenantRepository
{
    public async Task<Tenant?> GetByKeyAsync(Guid tenantKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<TenantDto>(
            Sql.Builder.Where("tenantKey = @0", tenantKey));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllActiveAsync()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TenantDto>(
            Sql.Builder.Where("isActive = @0", true));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<TenantDto>(
            Sql.Builder.OrderBy("createdAtUtc"));
        return dtos.Select(Map).ToList();
    }

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new TenantDto
        {
            TenantKey = tenant.TenantKey,
            Name = tenant.Name,
            ShortCode = tenant.ShortCode,
            IsActive = Tenant.IsUsableStatus(tenant.Status),
            Status = (int)tenant.Status,
            Plan = tenant.Plan,
            TrialEndsAtUtc = tenant.TrialEndsAtUtc,
            CreatedAtUtc = tenant.CreatedAtUtc,
            UpdatedAtUtc = tenant.UpdatedAtUtc
        });

        scope.Complete();
        return tenant with { IsActive = Tenant.IsUsableStatus(tenant.Status) };
    }

    public async Task<Tenant> UpdateAsync(Tenant tenant)
    {
        using var scope = scopeProvider.CreateScope();

        var dto = await scope.Database.FirstOrDefaultAsync<TenantDto>(
            Sql.Builder.Where("tenantKey = @0", tenant.TenantKey))
            ?? throw new InvalidOperationException($"Tenant {tenant.TenantKey} not found.");

        dto.Name = tenant.Name;
        dto.Status = (int)tenant.Status;
        dto.Plan = tenant.Plan;
        dto.TrialEndsAtUtc = tenant.TrialEndsAtUtc;
        dto.IsActive = Tenant.IsUsableStatus(tenant.Status);
        dto.UpdatedAtUtc = tenant.UpdatedAtUtc;

        await scope.Database.UpdateAsync(dto);
        scope.Complete();

        return Map(dto);
    }

    private static Tenant Map(TenantDto dto) => new()
    {
        TenantKey = dto.TenantKey,
        Name = dto.Name,
        ShortCode = dto.ShortCode,
        IsActive = dto.IsActive,
        // A null status/plan is a row that pre-dates AddTenantLifecycleColumns
        // and somehow escaped its backfill — read it with the same meaning
        // the backfill would have written.
        Status = dto.Status is null
            ? (dto.IsActive ? TenantStatus.Active : TenantStatus.Suspended)
            : (TenantStatus)dto.Status.Value,
        Plan = dto.Plan ?? TenantPlan.Enterprise,
        TrialEndsAtUtc = dto.TrialEndsAtUtc,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc
    };
}
