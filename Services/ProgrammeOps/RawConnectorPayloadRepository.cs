using NPoco;
using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class RawConnectorPayloadRepository(IScopeProvider scopes, IDataProtectionProvider dataProtection) : IRawConnectorPayloadRepository
{
    private readonly IDataProtector payloadProtector = dataProtection.CreateProtector("ProgrammePulse.RawConnectorPayload.v1");
    public async Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType,
        string externalId, string payloadJson, DateTime fetchedAtUtc)
    {
        using var scope = scopes.CreateScope();
        await scope.Database.InsertAsync(new RawConnectorPayloadDto
        {
            TenantId = tenantId, Source = source, SourceAccountId = sourceAccountId,
            EntityType = entityType, ExternalId = externalId,
            PayloadJson = payloadProtector.Protect(payloadJson), FetchedAtUtc = fetchedAtUtc
        });
        scope.Complete();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        using var scope = scopes.CreateScope();
        var deleted = await scope.Database.ExecuteAsync(
            Sql.Builder.Append($"DELETE FROM {RawConnectorPayloadDto.TableName} WHERE fetchedAtUtc < @0", cutoffUtc));
        scope.Complete();
        return deleted;
    }
}
