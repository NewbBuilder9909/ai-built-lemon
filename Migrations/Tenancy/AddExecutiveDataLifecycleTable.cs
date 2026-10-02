using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

public sealed class AddExecutiveDataLifecycleTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ExecutiveDataEventDto.TableName))
        {
            Create.Table<ExecutiveDataEventDto>().Do();
            Database.Execute("CREATE INDEX IX_ExecutiveReview_DataEvent_TenantTime ON ExecutiveReview_DataEvent (tenantId, recordedAtUtc)");
        }
        return Task.CompletedTask;
    }
}
