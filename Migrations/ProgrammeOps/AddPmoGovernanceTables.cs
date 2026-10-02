using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// PMO governance extension: a once-locked baseline snapshot per workstream,
/// a change-control record per project, and a RACI stakeholder register per
/// programme. New step rather than editing earlier ones in place, per the
/// append-only migration convention documented on AddProgrammeOpsTables.
/// </summary>
public sealed class AddPmoGovernanceTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, WorkstreamBaselineDto.TableName))
        {
            Create.Table<WorkstreamBaselineDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ChangeRequestDto.TableName))
        {
            Create.Table<ChangeRequestDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ProgrammeStakeholderDto.TableName))
        {
            Create.Table<ProgrammeStakeholderDto>().Do();
        }

        return Task.CompletedTask;
    }
}
