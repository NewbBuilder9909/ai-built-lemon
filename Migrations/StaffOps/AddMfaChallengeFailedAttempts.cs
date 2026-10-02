using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds StaffOps_MfaChallenge.failedAttempts to track and limit invalid MFA
/// code submissions per challenge, closing a distributed brute-force path
/// where distinct source IPs could exhaust the six-digit TOTP space against
/// a single five-minute challenge. New step rather than editing
/// ProtectMfaSecrets in place, per the append-only convention.
/// </summary>
public sealed class AddMfaChallengeFailedAttempts(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(MemberMfaChallengeDto.TableName, "failedAttempts"))
        {
            Alter.Table(MemberMfaChallengeDto.TableName)
                .AddColumn("failedAttempts").AsInt32().NotNullable().WithDefaultValue(0)
                .Do();
        }

        return Task.CompletedTask;
    }
}
