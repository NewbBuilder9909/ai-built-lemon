using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>Records ProgrammeOps audit entries so a service test can assert what was audited.</summary>
public sealed class FakeAuditLogRepository : IAuditLogRepository
{
    public List<(string EntityType, string EntityId, string Action, int? ActorMemberId, string? DetailJson, Guid TenantId)> Entries { get; } = [];

    public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        Entries.Add((entityType, entityId, action, actorMemberId, detailJson, tenantId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take, Guid tenantId) => Task.FromResult<IReadOnlyList<AuditLog>>([]);
}
