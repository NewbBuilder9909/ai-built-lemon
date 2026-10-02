using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds StaffOps_MemberMfa.recoveryCodesJson — one-time recovery codes so an
/// Admin who loses their authenticator can sign in without another Admin
/// having to reset their enrollment (see MfaRecoveryCodeGenerator and
/// StaffAccountController's recovery-code challenge action). New step rather
/// than editing AddMemberMfaTable in place, per the append-only convention.
/// </summary>
public sealed class AddMemberMfaRecoveryCodes(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(MemberMfaDto.TableName, "recoveryCodesJson"))
        {
            Alter.Table(MemberMfaDto.TableName)
                .AddColumn("recoveryCodesJson").AsString(2000).Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
