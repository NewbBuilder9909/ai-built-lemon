using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.SkillsEvidence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class SkillsEvidenceAuditLogRepository(IScopeProvider scopeProvider) : ISkillsEvidenceAuditLogRepository
{
    public async Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        await scope.Database.InsertAsync(new SkillsEvidenceAuditLogDto
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

    public async Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetRecentAsync(int take, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var page = await scope.Database.PageAsync<SkillsEvidenceAuditLogDto>(1, take,
            Sql.Builder.Select("*").From(SkillsEvidenceAuditLogDto.TableName)
                .Where("tenantId = @0", tenantId)
                .OrderBy("timestampUtc DESC", "id DESC"));
        return page.Items.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetForEntityAsync(string entityId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SkillsEvidenceAuditLogDto>(
            Sql.Builder.Where("entityId = @0 AND tenantId = @1", entityId, tenantId).OrderBy("timestampUtc", "id"));
        return dtos.Select(Map).ToList();
    }

    private static SkillsEvidenceAuditLog Map(SkillsEvidenceAuditLogDto dto) => new()
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
