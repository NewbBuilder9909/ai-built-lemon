using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// ProgrammeOps_SyncLease originally had one row per source, enforced by a
/// unique index on `source` alone (IX_ProgrammeOps_SyncLease_source). Now
/// that AddProgrammeTenantColumns has added tenantId to this table, that
/// index is wrong: a second tenant's first ClickUp/Hub Planner sync would
/// violate it trying to insert its own "ClickUp"/"HubPlanner" row. This
/// drops the old index and replaces it with one on (tenantId, source), so
/// each tenant gets its own lease row per source and one tenant's sync can
/// no longer serialize against another's (see SyncRunRepository, which now
/// keys every lease/run operation on (tenantId, source)).
/// </summary>
public sealed class RekeySyncLeaseByTenant(IMigrationContext context) : AsyncMigrationBase(context)
{
    private const string OldIndexName = "IX_ProgrammeOps_SyncLease_source";
    public const string TenantSourceIndexName = "UX_ProgrammeOps_SyncLease_tenant_source";

    protected override Task MigrateAsync()
    {
        var table = SyncLeaseDto.TableName;

        Database.Execute(
            $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{OldIndexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"DROP INDEX [{OldIndexName}] ON [{table}]");

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{TenantSourceIndexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{TenantSourceIndexName}] ON [{table}] ([tenantId], [source])");

        return Task.CompletedTask;
    }
}
