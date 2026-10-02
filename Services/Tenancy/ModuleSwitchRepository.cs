using NPoco;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Tenancy;

public interface IModuleSwitchRepository
{
    Task<IReadOnlySet<string>> GetSwitchedOnAsync(Guid tenantId);

    /// <summary>Switches a module on or off. Idempotent: switching on twice keeps the first row.</summary>
    Task SetAsync(Guid tenantId, string moduleKey, bool on, int? actorMemberId, DateTime nowUtc);
}

public sealed class ModuleSwitchRepository(IScopeProvider scopeProvider) : IModuleSwitchRepository
{
    public async Task<IReadOnlySet<string>> GetSwitchedOnAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<TenantModuleSwitchDto>(
            Sql.Builder.Where("tenantId = @0", tenantId));
        return new HashSet<string>(rows.Select(r => r.ModuleKey), StringComparer.Ordinal);
    }

    public async Task SetAsync(Guid tenantId, string moduleKey, bool on, int? actorMemberId, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        if (on)
        {
            await scope.Database.ExecuteAsync(
                $"IF NOT EXISTS (SELECT 1 FROM [{TenantModuleSwitchDto.TableName}] WHERE [tenantId] = @0 AND [moduleKey] = @1) " +
                $"INSERT INTO [{TenantModuleSwitchDto.TableName}] ([tenantId], [moduleKey], [switchedOnAtUtc], [switchedOnByMemberId]) VALUES (@0, @1, @2, @3)",
                tenantId, moduleKey, nowUtc, actorMemberId);
        }
        else
        {
            await scope.Database.ExecuteAsync(
                $"DELETE FROM [{TenantModuleSwitchDto.TableName}] WHERE [tenantId] = @0 AND [moduleKey] = @1",
                tenantId, moduleKey);
        }

        scope.Complete();
    }
}
