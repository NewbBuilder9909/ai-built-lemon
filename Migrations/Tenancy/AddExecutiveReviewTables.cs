using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

public sealed class AddExecutiveReviewTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, MarketSettingsVersionDto.TableName))
        {
            Create.Table<MarketSettingsVersionDto>().Do();
            Database.Execute("CREATE UNIQUE INDEX IX_ExecutiveReview_MarketVersion_TenantVersion ON ExecutiveReview_MarketVersion (tenantId, version)");
        }
        if (!SqlSyntax.DoesTableExist(Database, ExecutivePackDto.TableName))
        {
            Create.Table<ExecutivePackDto>().Do();
            Database.Execute("CREATE INDEX IX_ExecutiveReview_Pack_TenantCaptured ON ExecutiveReview_Pack (tenantId, capturedAtUtc)");
        }
        return Task.CompletedTask;
    }
}
