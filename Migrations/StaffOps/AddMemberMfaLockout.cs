using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds a per-member MFA lockout to StaffOps_MemberMfa: failedAttempts and
/// lockedUntilUtc. The per-challenge budget (AddMfaChallengeFailedAttempts)
/// stops guessing within one sign-in, but anyone holding the password can
/// start a new challenge and get a fresh budget; this counter spans
/// challenges (Aikido: excessive authentication attempts). Idempotent.
/// </summary>
public sealed class AddMemberMfaLockout(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(MemberMfaDto.TableName, "failedAttempts"))
        {
            Alter.Table(MemberMfaDto.TableName)
                .AddColumn("failedAttempts").AsInt32().NotNullable().WithDefaultValue(0)
                .Do();
        }

        if (!ColumnExists(MemberMfaDto.TableName, "lockedUntilUtc"))
        {
            Alter.Table(MemberMfaDto.TableName)
                .AddColumn("lockedUntilUtc").AsDateTime().Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
