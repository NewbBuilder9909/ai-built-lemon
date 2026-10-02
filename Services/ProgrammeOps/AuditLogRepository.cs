using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class AuditLogRepository(IScopeProvider scopeProvider) : IAuditLogRepository
{
    public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new AuditLogDto
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

    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var page = await scope.Database.PageAsync<AuditLogDto>(1, take,
            Sql.Builder.Select("*").From(AuditLogDto.TableName)
                .Where("tenantId = @0", tenantId)
                .OrderBy("timestampUtc DESC", "id DESC"));
        return page.Items.Select(Map).ToList();
    }

    private static AuditLog Map(AuditLogDto dto) => new()
    {
        LogKey = dto.LogKey,
        TenantId = dto.TenantId,
        EntityType = dto.EntityType,
        EntityId = dto.EntityId,
        Action = dto.Action,
        ActorMemberId = dto.ActorMemberId,
        DetailJson = dto.DetailJson,
        TimestampUtc = dto.TimestampUtc
    };
}
