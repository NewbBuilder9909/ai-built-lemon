using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Adds the Bronze raw-capture table for the Hub Planner integration — a
/// second, independent Bronze source for the same ProgrammeOps schema
/// AddProgrammeOpsTables already created for ClickUp. Idempotent via
/// DoesTableExist, same as every other step in this plan.
/// </summary>
public sealed class AddHubPlannerRawTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, RawHubPlannerPayloadDto.TableName))
        {
            Create.Table<RawHubPlannerPayloadDto>().Do();
        }

        return Task.CompletedTask;
    }
}
