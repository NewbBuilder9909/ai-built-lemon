using System.Text.Json;
using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Every Programme Ops table that holds a tenant's delivery data. All carry
/// <c>tenantId</c> and none has a foreign key, so deletion order doesn't
/// matter. DeliveryDataPurgeTests fails the build if a ProgrammeOps table
/// with a tenantId column is added without being listed here, because a
/// purge that silently misses a table is worse than no purge.
/// </summary>
public static class DeliveryDataTables
{
    public static readonly IReadOnlyList<string> All =
    [
        ProgrammeDto.TableName, ProjectDto.TableName, WorkstreamDto.TableName, WorkItemDto.TableName,
        WorkItemAllocationDto.TableName, DependencyDto.TableName, RiskDto.TableName, IssueDto.TableName,
        AuditLogDto.TableName, RawClickUpPayloadDto.TableName, RawHubPlannerPayloadDto.TableName,
        RawConnectorPayloadDto.TableName, TimeEntryDto.TableName, CustomerDto.TableName,
        WorkstreamBaselineDto.TableName, ChangeRequestDto.TableName, ProgrammeStakeholderDto.TableName,
        ReportingSnapshotDto.TableName, AlertDto.TableName, PlannedAllocationDto.TableName,
        ExternalIdentityLinkDto.TableName, UnresolvedIdentityDto.TableName, SyncRunDto.TableName,
        SyncLeaseDto.TableName, SourceConnectionDto.TableName, EstimateBaselineDto.TableName,
        EvidenceReviewDto.TableName, EvidenceReviewFindingDto.TableName, CodeRepositoryLinkDto.TableName,
        ImportStagingDto.TableName
    ];
}

public sealed record TableRowCount(string Table, int Rows);

public interface IDeliveryDataPurgeRepository
{
    Task<IReadOnlyList<TableRowCount>> CountAsync(Guid tenantId);

    /// <summary>Deletes every row for the tenant from every table in one transaction; returns what was removed.</summary>
    Task<IReadOnlyList<TableRowCount>> PurgeAsync(Guid tenantId);
}

public sealed class DeliveryDataPurgeRepository(IScopeProvider scopeProvider) : IDeliveryDataPurgeRepository
{
    public async Task<IReadOnlyList<TableRowCount>> CountAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var counts = new List<TableRowCount>();
        foreach (var table in DeliveryDataTables.All)
            counts.Add(new(table, await scope.Database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE tenantId = @0", tenantId)));
        return counts;
    }

    public async Task<IReadOnlyList<TableRowCount>> PurgeAsync(Guid tenantId)
    {
        // Table names come only from the compile-time list above, never from input.
        using var scope = scopeProvider.CreateScope();
        var removed = new List<TableRowCount>();
        foreach (var table in DeliveryDataTables.All)
            removed.Add(new(table, await scope.Database.ExecuteAsync(new Sql($"DELETE FROM {table} WHERE tenantId = @0", tenantId))));
        scope.Complete();
        return removed;
    }
}

public sealed record DeliveryDataPurgePreview(Tenant Tenant, IReadOnlyList<TableRowCount> Counts, string? RefusalReason)
{
    public int TotalRows => Counts.Sum(c => c.Rows);

    public bool CanPurge => RefusalReason is null;
}

public sealed record DeliveryDataPurgeResult(bool Purged, string? Error, IReadOnlyList<TableRowCount> Removed, DateTime? PurgedAtUtc)
{
    public int TotalRows => Removed.Sum(c => c.Rows);
}

public interface IDeliveryDataPurgeService
{
    Task<DeliveryDataPurgePreview?> PreviewAsync(Guid tenantKey);

    /// <summary>Also writes the "DeliveryDataPurged" row to StaffOps_AuditLog, which the purge doesn't touch.</summary>
    Task<DeliveryDataPurgeResult> PurgeAsync(Guid tenantKey, string? confirmShortCode, int? actorMemberId = null);
}

/// <summary>
/// Deletes one customer's delivery data at the end of a diagnostic, the
/// promise in docs/commercial/diagnostic-data-handling.md. Two safeguards,
/// both enforced here rather than in the page: the tenant must already be
/// Suspended or Archived, so nobody can be importing while it runs and a live
/// customer can't be wiped by a mis-click; and the operator must type the
/// tenant's short code. Staff profiles and members are not touched: people
/// go through the per-person GDPR erasure route, which also covers the
/// other feature areas they appear in.
/// </summary>
public sealed class DeliveryDataPurgeService(
    ITenantRepository tenants,
    IDeliveryDataPurgeRepository purge,
    IStaffAuditLogRepository audit,
    TimeProvider clock) : IDeliveryDataPurgeService
{
    public async Task<DeliveryDataPurgePreview?> PreviewAsync(Guid tenantKey)
    {
        var tenant = await tenants.GetByKeyAsync(tenantKey);
        if (tenant is null)
            return null;
        return new(tenant, await purge.CountAsync(tenantKey), RefusalFor(tenant));
    }

    public async Task<DeliveryDataPurgeResult> PurgeAsync(Guid tenantKey, string? confirmShortCode, int? actorMemberId = null)
    {
        var tenant = await tenants.GetByKeyAsync(tenantKey);
        if (tenant is null)
            return new(false, "No such tenant.", [], null);
        if (RefusalFor(tenant) is { } refusal)
            return new(false, refusal, [], null);
        if (!string.Equals(confirmShortCode?.Trim(), tenant.ShortCode, StringComparison.OrdinalIgnoreCase))
            return new(false, $"Type the tenant's short code, {tenant.ShortCode}, to confirm.", [], null);

        var removed = await purge.PurgeAsync(tenantKey);
        var now = clock.GetUtcNow().UtcDateTime;

        // Recorded under the purged tenant, so its Admin sees it too, and in
        // StaffOps_AuditLog, which outlives the purge as its record.
        await audit.LogAsync("Tenant", tenantKey.ToString(), "DeliveryDataPurged", actorMemberId,
            JsonSerializer.Serialize(new
            {
                totalRows = removed.Sum(r => r.Rows),
                tables = removed.Where(r => r.Rows > 0).ToDictionary(r => r.Table, r => r.Rows)
            }), now, tenantKey);

        return new(true, null, removed, now);
    }

    private static string? RefusalFor(Tenant tenant) =>
        tenant.Status is TenantStatus.Suspended or TenantStatus.Archived
            ? null
            : $"'{tenant.Name}' is {tenant.Status}. Suspend or archive it first, so nobody can use or import into it while its data is deleted.";
}
