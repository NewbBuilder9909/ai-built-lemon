using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

public sealed class AddExecutiveDecisionTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ExecutiveDecisionVersionDto.TableName))
        {
            Create.Table<ExecutiveDecisionVersionDto>().Do();
            Database.Execute("CREATE UNIQUE INDEX IX_ExecutiveReview_DecisionVersion_TenantKeyVersion ON ExecutiveReview_DecisionVersion (tenantId, decisionKey, version)");
        }
        return Task.CompletedTask;
    }
}
