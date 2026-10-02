using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Weekly-hours history — see Models/Staff/WorkHoursHistory. New step
/// rather than editing earlier ones in place, per the append-only
/// convention. Existing staff have no history rows until they next go
/// through StaffAdminController.SetWorkHours; ReportingQueryService's
/// history lookup falls back to StaffProfile.DefaultWorkHoursPerWeek for
/// any day not covered by a history row, so this is a safe, silent gap
/// rather than a broken calculation for pre-existing staff.
/// </summary>
public sealed class AddWorkHoursHistoryTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, WorkHoursHistoryDto.TableName))
        {
            Create.Table<WorkHoursHistoryDto>().Do();
        }

        return Task.CompletedTask;
    }
}
