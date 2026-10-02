using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Creates the four StaffOps tables. Idempotent via DoesTableExist checks so it's
/// safe if the plan is ever re-run against a database that already has them.
/// </summary>
public sealed class AddStaffOpsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, StaffDto.TableName))
        {
            Create.Table<StaffDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, StaffRateDto.TableName))
        {
            Create.Table<StaffRateDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, AvailabilityDto.TableName))
        {
            Create.Table<AvailabilityDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, LeaveRequestDto.TableName))
        {
            Create.Table<LeaveRequestDto>().Do();
        }

        return Task.CompletedTask;
    }
}
