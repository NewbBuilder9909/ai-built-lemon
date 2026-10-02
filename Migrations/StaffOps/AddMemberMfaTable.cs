using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// TOTP MFA enrollment, one row per Umbraco Member. New step rather than
/// editing AddStaffOpsTables in place, per the append-only convention.
/// </summary>
public sealed class AddMemberMfaTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, MemberMfaDto.TableName))
        {
            Create.Table<MemberMfaDto>().Do();
        }

        return Task.CompletedTask;
    }
}
