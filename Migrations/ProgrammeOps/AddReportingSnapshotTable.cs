using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Period-close reporting snapshots, capturing the Reporting Hub's
/// portfolio-wide totals on demand so a trend view has history to show. New
/// step rather than editing earlier ones in place, per the append-only
/// convention.
/// </summary>
public sealed class AddReportingSnapshotTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ReportingSnapshotDto.TableName))
        {
            Create.Table<ReportingSnapshotDto>().Do();
        }

        return Task.CompletedTask;
    }
}
