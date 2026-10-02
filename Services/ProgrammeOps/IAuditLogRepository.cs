using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

public interface IAuditLogRepository
{
    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);

    /// <summary>Newest first; the Admin audit page (StaffAdminController.Audit).</summary>
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId);
}
