using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Staff;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Staff;

public sealed class StaffAuditLogRepository(IScopeProvider scopeProvider) : IStaffAuditLogRepository
{
    public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new StaffAuditLogDto
        {
            LogKey = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorMemberId = actorMemberId,
            TenantId = tenantId,
            DetailJson = detailJson,
            TimestampUtc = timestampUtc
        });

        scope.Complete();
    }

    public async Task<IReadOnlyList<StaffAuditLog>> GetRecentAsync(int take, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var page = await scope.Database.PageAsync<StaffAuditLogDto>(1, take,
            Sql.Builder.Select("*").From(StaffAuditLogDto.TableName)
                .Where("tenantId = @0", tenantId)
                .OrderBy("timestampUtc DESC", "id DESC"));
        return page.Items.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffAuditLog>> GetForEntityAsync(string entityId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<StaffAuditLogDto>(
            Sql.Builder.Where("tenantId = @0 AND entityId = @1", tenantId, entityId).OrderBy("timestampUtc", "id"));
        return dtos.Select(Map).ToList();
    }

    private static StaffAuditLog Map(StaffAuditLogDto dto) => new()
    {
        LogKey = dto.LogKey,
        EntityType = dto.EntityType,
        EntityId = dto.EntityId,
        Action = dto.Action,
        ActorMemberId = dto.ActorMemberId,
        TenantId = dto.TenantId,
        DetailJson = dto.DetailJson,
        TimestampUtc = dto.TimestampUtc
    };
}
