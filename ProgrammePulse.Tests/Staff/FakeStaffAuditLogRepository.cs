using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Staff;

public sealed class FakeStaffAuditLogRepository : IStaffAuditLogRepository
{
    public readonly List<StaffAuditLog> Entries = [];

    public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId)
    {
        Entries.Add(new StaffAuditLog
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
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StaffAuditLog>> GetRecentAsync(int take, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<StaffAuditLog>>(Entries.Where(e => e.TenantId == tenantId).OrderByDescending(e => e.TimestampUtc).Take(take).ToList());

    public Task<IReadOnlyList<StaffAuditLog>> GetForEntityAsync(string entityId, Guid tenantId) =>
        Task.FromResult<IReadOnlyList<StaffAuditLog>>(Entries.Where(e => e.TenantId == tenantId && e.EntityId == entityId).OrderBy(e => e.TimestampUtc).ToList());
}
