using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Adds the billable/non-billable flag ClickUp exposes on time entries but
/// the Bronze DTO didn't originally capture. New step rather than editing
/// AddReportingTables in place — that step already ran on this dev DB.
/// </summary>
public sealed class AddTimeEntryBillableFlag(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(TimeEntryDto.TableName, "isBillable"))
        {
            Alter.Table(TimeEntryDto.TableName)
                .AddColumn("isBillable").AsBoolean().NotNullable().WithDefaultValue(true)
                .Do();
        }

        return Task.CompletedTask;
    }
}
