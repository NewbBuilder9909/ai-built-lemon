using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Durable sync-run state (ProgrammeOps_SyncRun) and the per-source
/// database lease (ProgrammeOps_SyncLease) that replaces the
/// process-local-only concurrency guard for multi-instance deployments —
/// see Models/Programme/SyncRun and Services/ProgrammeOps/SyncRunRepository.
/// The lease rows themselves are created lazily on first acquisition, not
/// seeded here, so adding a third source never needs a migration.
/// </summary>
public sealed class AddSyncRunTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, SyncRunDto.TableName))
        {
            Create.Table<SyncRunDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, SyncLeaseDto.TableName))
        {
            Create.Table<SyncLeaseDto>().Do();
        }

        return Task.CompletedTask;
    }
}
