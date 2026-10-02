using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// The in-app alert/notification table — see Models/Programme/Alert.cs.
/// New step rather than editing earlier ones in place, per the
/// append-only convention.
/// </summary>
public sealed class AddAlertTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, AlertDto.TableName))
        {
            Create.Table<AlertDto>().Do();
        }

        return Task.CompletedTask;
    }
}
