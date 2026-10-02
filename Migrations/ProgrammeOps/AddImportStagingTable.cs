using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>Holds a replacement import between its preview and the person's confirmation. tenantId is non-nullable.</summary>
public sealed class AddImportStagingTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ImportStagingDto.TableName))
            Create.Table<ImportStagingDto>().Do();
        return Task.CompletedTask;
    }
}
