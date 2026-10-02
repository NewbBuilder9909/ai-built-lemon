namespace ProgrammePulse.Services.BrandingOps;

public interface IBrandingAuditLogRepository
{
    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);
}
