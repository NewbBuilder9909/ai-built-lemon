using ProgrammePulse.Data.Dtos;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Tenancy;

public sealed class TenantFeatureSelectionRepository(IScopeProvider scopeProvider) : ITenantFeatureSelectionRepository
{
    public async Task<IReadOnlySet<string>> GetSelectedFeaturesAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var rows = await scope.Database.FetchAsync<TenantFeatureSelectionDto>(
            Sql.Builder.Where("tenantId = @0", tenantId));
        return new HashSet<string>(rows.Select(r => r.FeatureKey), StringComparer.OrdinalIgnoreCase);
    }

    public async Task ReplaceAsync(Guid tenantId, IEnumerable<string> featureKeys, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync($"DELETE FROM [{TenantFeatureSelectionDto.TableName}] WHERE [tenantId] = @0", tenantId);

        var distinct = featureKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var featureKey in distinct)
        {
            await scope.Database.InsertAsync(new TenantFeatureSelectionDto
            {
                TenantId = tenantId,
                FeatureKey = featureKey,
                CreatedAtUtc = nowUtc
            });
        }

        scope.Complete();
    }
}
