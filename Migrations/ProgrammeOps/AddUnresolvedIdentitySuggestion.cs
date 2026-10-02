using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Adds suggestedStaffKey to ProgrammeOps_UnresolvedIdentity. An email match
/// used to attribute a source-tool person to a staff profile outright; it is
/// now only a suggestion on the identity queue that an Admin approves. Rows
/// already resolved keep their history. Idempotent.
/// </summary>
public sealed class AddUnresolvedIdentitySuggestion(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(UnresolvedIdentityDto.TableName, "suggestedStaffKey"))
            Alter.Table(UnresolvedIdentityDto.TableName).AddColumn("suggestedStaffKey")
                .AsGuid().Nullable().Do();
        return Task.CompletedTask;
    }
}
