using System.Reflection;
using ProgrammePulse.Tests.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The end-of-diagnostic deletion promised in
/// docs/commercial/diagnostic-data-handling.md. The first test is the
/// important one: a purge that silently misses a table is a broken promise
/// to a customer, so adding a tenant-scoped ProgrammeOps table without
/// listing it in DeliveryDataTables fails the build.
/// </summary>
public class DeliveryDataPurgeTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_tenant_scoped_ProgrammeOps_table_is_purged()
    {
        var tenantScoped = typeof(DeliveryDataTables).Assembly.GetTypes()
            .Where(t => t.Namespace == "ProgrammePulse.Data.Dtos")
            .Select(t => (Type: t, Table: t.GetField("TableName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string))
            .Where(x => x.Table is not null && x.Table.StartsWith("ProgrammeOps_", StringComparison.Ordinal))
            .Where(x => x.Type.GetProperties().Any(p => p.GetCustomAttributes()
                .Any(a => a.GetType().Name == "ColumnAttribute" && (a.GetType().GetProperty("Name")?.GetValue(a) as string) == "tenantId")))
            .Select(x => x.Table!)
            .ToHashSet();

        Assert.NotEmpty(tenantScoped);
        Assert.Equal(tenantScoped.Order(), DeliveryDataTables.All.Order());
    }

    [Theory]
    [InlineData(TenantStatus.Trial)]
    [InlineData(TenantStatus.Active)]
    public async Task A_tenant_still_in_use_is_never_purged(TenantStatus status)
    {
        var harness = new Harness(status);

        var preview = await harness.Service.PreviewAsync(harness.Tenant.TenantKey);
        var result = await harness.Service.PurgeAsync(harness.Tenant.TenantKey, harness.Tenant.ShortCode);

        Assert.False(preview!.CanPurge);
        Assert.False(result.Purged);
        Assert.Contains("Suspend or archive it first", result.Error);
        Assert.Equal(0, harness.Repository.PurgeCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("someone-else")]
    public async Task The_short_code_must_be_typed_to_confirm(string? typed)
    {
        var harness = new Harness(TenantStatus.Archived);

        var result = await harness.Service.PurgeAsync(harness.Tenant.TenantKey, typed);

        Assert.False(result.Purged);
        Assert.Contains(harness.Tenant.ShortCode, result.Error);
        Assert.Equal(0, harness.Repository.PurgeCalls);
        Assert.Empty(harness.Audit.Entries);
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Archived)]
    public async Task A_confirmed_purge_of_a_closed_tenant_runs_once_and_reports_what_it_removed(TenantStatus status)
    {
        var harness = new Harness(status);

        var result = await harness.Service.PurgeAsync(harness.Tenant.TenantKey, $" {harness.Tenant.ShortCode.ToUpperInvariant()} ");

        Assert.True(result.Purged, result.Error);
        Assert.Equal(1, harness.Repository.PurgeCalls);
        Assert.Equal(harness.Tenant.TenantKey, harness.Repository.PurgedTenant);
        Assert.Equal(12, result.TotalRows);
        Assert.Equal(Now, result.PurgedAtUtc);

        // The record of the deletion, under the purged tenant, in a table the purge doesn't touch.
        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal("DeliveryDataPurged", entry.Action);
        Assert.Equal(harness.Tenant.TenantKey, entry.TenantId);
        Assert.Contains("\"totalRows\":12", entry.DetailJson);
    }

    [Fact]
    public async Task An_unknown_tenant_has_no_preview_and_purges_nothing()
    {
        var harness = new Harness(TenantStatus.Archived);

        Assert.Null(await harness.Service.PreviewAsync(Guid.NewGuid()));
        Assert.False((await harness.Service.PurgeAsync(Guid.NewGuid(), "anything")).Purged);
        Assert.Equal(0, harness.Repository.PurgeCalls);
    }

    private sealed class Harness
    {
        public Harness(TenantStatus status)
        {
            Tenant = new Tenant
            {
                TenantKey = Guid.NewGuid(), Name = "Acme", ShortCode = "acme", Status = status,
                IsActive = Tenant.IsUsableStatus(status), CreatedAtUtc = Now
            };
            Service = new DeliveryDataPurgeService(new FakeTenants(Tenant), Repository, Audit, new FixedClock(Now));
        }

        public Tenant Tenant { get; }

        public FakeStaffAuditLogRepository Audit { get; } = new();
        public FakePurgeRepository Repository { get; } = new();
        public DeliveryDataPurgeService Service { get; }
    }

    private sealed class FakeTenants(Tenant tenant) : ITenantRepository
    {
        public Task<Tenant?> GetByKeyAsync(Guid tenantKey) => Task.FromResult(tenantKey == tenant.TenantKey ? tenant : null);
        public Task<IReadOnlyList<Tenant>> GetAllActiveAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<Tenant>> GetAllAsync() => throw new NotSupportedException();
        public Task<Tenant> CreateAsync(Tenant created) => throw new NotSupportedException();
        public Task<Tenant> UpdateAsync(Tenant updated) => throw new NotSupportedException();
    }

    private sealed class FakePurgeRepository : IDeliveryDataPurgeRepository
    {
        private static readonly IReadOnlyList<TableRowCount> Held = [new("ProgrammeOps_WorkItem", 7), new("ProgrammeOps_TimeEntry", 5)];

        public int PurgeCalls { get; private set; }
        public Guid? PurgedTenant { get; private set; }

        public Task<IReadOnlyList<TableRowCount>> CountAsync(Guid tenantId) => Task.FromResult(Held);

        public Task<IReadOnlyList<TableRowCount>> PurgeAsync(Guid tenantId)
        {
            PurgeCalls++;
            PurgedTenant = tenantId;
            return Task.FromResult(Held);
        }
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
