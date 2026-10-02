using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Explicit identity links and the unresolved-identity queue — see
/// Models/Programme/ExternalIdentityLink and UnresolvedIdentity, and
/// Services/ProgrammeOps/StaffIdentityResolver for how the two are used.
/// New step rather than editing earlier ones in place, per the append-only
/// convention.
/// </summary>
public sealed class AddIdentityResolutionTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ExternalIdentityLinkDto.TableName))
        {
            Create.Table<ExternalIdentityLinkDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, UnresolvedIdentityDto.TableName))
        {
            Create.Table<UnresolvedIdentityDto>().Do();
        }

        return Task.CompletedTask;
    }
}
