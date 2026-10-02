using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.BrandingOps;

public sealed class BrandingAuditLogRepository(IScopeProvider scopeProvider) : IBrandingAuditLogRepository
{
    public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new BrandingAuditLogDto
        {
            LogKey = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorMemberId = actorMemberId,
            DetailJson = detailJson,
            TimestampUtc = timestampUtc
        });

        scope.Complete();
    }
}
