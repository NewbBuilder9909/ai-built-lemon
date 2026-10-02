using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Phase 3 reporting extension: TimeEntry (Silver, mapped from ClickUp time
/// entries) and Customer (admin-authored, not ClickUp-sourced), plus the
/// nullable Programme.CustomerKey column linking the two. A new migration
/// step rather than editing AddProgrammeOpsTables in place, per the
/// append-only migration convention documented there.
/// </summary>
public sealed class AddReportingTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, TimeEntryDto.TableName))
        {
            Create.Table<TimeEntryDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, CustomerDto.TableName))
        {
            Create.Table<CustomerDto>().Do();
        }

        if (!ColumnExists(ProgrammeDto.TableName, "customerKey"))
        {
            Alter.Table(ProgrammeDto.TableName)
                .AddColumn("customerKey").AsGuid().Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
