using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>Preserves the provider's work calendar date independently of a UTC start instant.</summary>
public sealed class AddTimeEntryWorkDate(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(TimeEntryDto.TableName, "workDate"))
            Alter.Table(TimeEntryDto.TableName).AddColumn("workDate").AsDate().Nullable().Do();

        Execute.Sql($"UPDATE {TimeEntryDto.TableName} SET workDate = CONVERT(date, startedAtUtc) WHERE workDate IS NULL AND startedAtUtc IS NOT NULL").Do();
        return Task.CompletedTask;
    }
}
