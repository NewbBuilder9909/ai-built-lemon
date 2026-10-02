namespace ProgrammePulse.Services.ContractOps;

public interface IContractAuditLogRepository
{
    Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId);
}
