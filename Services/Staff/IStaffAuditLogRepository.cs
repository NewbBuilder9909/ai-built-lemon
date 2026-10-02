using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

public interface IStaffAuditLogRepository
{
    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);

    /// <summary>Newest first; the Admin audit page (StaffAdminController.Audit).</summary>
    Task<IReadOnlyList<StaffAuditLog>> GetRecentAsync(int take, Guid tenantId);

    /// <summary>
    /// Every entry this tenant recorded against one entity id (e.g. a StaffKey) — included in the GDPR export.
    /// Tenant-scoped even though the caller has already proven the subject is theirs: a subject's key is not
    /// a licence to read another tenant's rows about them (Aikido: cross-tenant isolation bypass).
    /// </summary>
    Task<IReadOnlyList<StaffAuditLog>> GetForEntityAsync(string entityId, Guid tenantId);
}
