using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

public interface ISkillsEvidenceAuditLogRepository
{
    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);

    /// <summary>Newest first, for one tenant. Nothing here reads across tenants.</summary>
    Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetRecentAsync(int take, Guid tenantId);

    /// <summary>Every entry recorded against one entity id — a StaffKey, a skill key, an assertion key.</summary>
    Task<IReadOnlyList<SkillsEvidenceAuditLog>> GetForEntityAsync(string entityId, Guid tenantId);
}
