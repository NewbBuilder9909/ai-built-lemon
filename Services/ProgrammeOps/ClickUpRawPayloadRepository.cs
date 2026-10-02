using ProgrammePulse.Data.Dtos;
using NPoco;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class ClickUpRawPayloadRepository(IScopeProvider scopeProvider) : IClickUpRawPayloadRepository
{
    public async Task SaveAsync(string entityType, string externalId, string workspaceId, string payloadJson, DateTime fetchedAtUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new RawClickUpPayloadDto
        {
            TenantId = tenantId,
            EntityType = entityType,
            ExternalId = externalId,
            WorkspaceId = workspaceId,
            PayloadJson = payloadJson,
            FetchedAtUtc = fetchedAtUtc
        });

        scope.Complete();
    }

    public async Task SaveManyAsync(string entityType, IReadOnlyList<(string ExternalId, string PayloadJson)> payloads, string workspaceId, DateTime fetchedAtUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        await SqlMultiRow.InsertAsync(scope.Database, RawClickUpPayloadDto.TableName,
            ["tenantId", "entityType", "externalId", "workspaceId", "payloadJson", "fetchedAtUtc"],
            payloads.Select(p => (object?[])[tenantId, entityType, p.ExternalId, workspaceId, p.PayloadJson, fetchedAtUtc]));
        scope.Complete();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            Sql.Builder.Append($"DELETE FROM {RawClickUpPayloadDto.TableName} WHERE fetchedAtUtc < @0", cutoffUtc));
        scope.Complete();
        return deleted;
    }
}
