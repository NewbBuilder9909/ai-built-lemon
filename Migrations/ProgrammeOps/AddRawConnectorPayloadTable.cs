using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

public sealed class AddRawConnectorPayloadTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, RawConnectorPayloadDto.TableName))
            Create.Table<RawConnectorPayloadDto>().Do();
        return Task.CompletedTask;
    }
}
