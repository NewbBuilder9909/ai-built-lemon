using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Split work allocation — a ClickUp task can carry multiple assignees; see
/// Models/Programme/WorkItemAllocation and
/// ClickUpMappingService.ResolveAssigneeStaffKeysAsync. New step rather than
/// editing earlier ones in place, per the append-only convention.
/// </summary>
public sealed class AddWorkItemAllocationTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, WorkItemAllocationDto.TableName))
        {
            Create.Table<WorkItemAllocationDto>().Do();
        }

        return Task.CompletedTask;
    }
}
