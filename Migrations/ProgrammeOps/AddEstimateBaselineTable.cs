using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

public sealed class AddEstimateBaselineTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, EstimateBaselineDto.TableName))
        {
            Create.Table<EstimateBaselineDto>().Do();
            Execute.Sql("CREATE UNIQUE INDEX UX_EstimateBaseline_tenant_work ON ProgrammeOps_EstimateBaseline (tenantId, workItemKey)").Do();
        }
        return Task.CompletedTask;
    }
}
