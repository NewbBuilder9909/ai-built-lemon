using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Keeps the source's id for the work item a time entry was logged against,
/// even when that item isn't synced, so unlinked hours can say which item
/// they belong to. Nullable: existing rows and sources that name no item
/// have none, and nothing is backfilled.
/// </summary>
public sealed class AddTimeEntrySourceWorkItem(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(TimeEntryDto.TableName, "sourceWorkItemExternalId"))
            Alter.Table(TimeEntryDto.TableName).AddColumn("sourceWorkItemExternalId")
                .AsString(128).Nullable().Do();
        return Task.CompletedTask;
    }
}
