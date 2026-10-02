using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Services.Security;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>Append-only upgrade. Encrypts legacy seeds before blanking their plaintext column.</summary>
public sealed class ProtectMfaSecrets(IMigrationContext context, MfaSecretProtection protection) : AsyncMigrationBase(context)
{
    protected override async Task MigrateAsync()
    {
        if (!ColumnExists(MemberMfaDto.TableName, "protectedTotpSecret"))
            Alter.Table(MemberMfaDto.TableName).AddColumn("protectedTotpSecret").AsString(2000).Nullable().Do();
        if (!ColumnExists(MemberMfaDto.TableName, "credentialVersion"))
            Alter.Table(MemberMfaDto.TableName).AddColumn("credentialVersion").AsGuid().Nullable().Do();
        if (!ColumnExists(MemberMfaDto.TableName, "lastAcceptedTimeStep"))
            Alter.Table(MemberMfaDto.TableName).AddColumn("lastAcceptedTimeStep").AsInt64().Nullable().Do();
        if (!SqlSyntax.DoesTableExist(Database, MemberMfaChallengeDto.TableName))
            Create.Table<MemberMfaChallengeDto>().Do();
        await EncryptLegacyRowsAsync(Database, protection);
    }

    internal static async Task EncryptLegacyRowsAsync(IUmbracoDatabase database, MfaSecretProtection protection)
    {
        var rows = await database.FetchAsync<MemberMfaDto>();
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.ProtectedTotpSecret))
                row.ProtectedTotpSecret = protection.Protect(row.MemberId, row.TotpSecret);
            row.CredentialVersion ??= Guid.NewGuid();
            row.TotpSecret = string.Empty;
            await database.UpdateAsync(row);
        }
    }
}
