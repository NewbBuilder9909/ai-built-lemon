using ProgrammePulse.Data.Dtos;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class HubPlannerRawPayloadRepository(IScopeProvider scopeProvider) : IHubPlannerRawPayloadRepository
{
    private const string ScopeId = "hubplanner";

    public async Task SaveAsync(string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new RawHubPlannerPayloadDto
        {
            TenantId = tenantId,
            EntityType = entityType,
            ExternalId = externalId,
            ScopeId = ScopeId,
            PayloadJson = payloadJson,
            FetchedAtUtc = fetchedAtUtc
        });

        scope.Complete();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            Sql.Builder.Append($"DELETE FROM {RawHubPlannerPayloadDto.TableName} WHERE fetchedAtUtc < @0", cutoffUtc));
        scope.Complete();
        return deleted;
    }
}
