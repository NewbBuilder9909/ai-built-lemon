using System.Text.Json;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// One external identity maps to at most one StaffProfile: filtered unique
/// indexes on (externalSource, externalUserId) and (externalSource, email)
/// over ProgrammeOps_ExternalIdentityLink. Before the indexes go on, any
/// duplicate rows are removed keeping the oldest (lowest id) per key — the
/// only way duplicates can exist is two admins linking the same queue row
/// before this constraint existed — and the removal is written to
/// ProgrammeOps_AuditLog with the counts so it is never a silent side
/// effect. Filtered indexes are T-SQL (the only provider this site runs
/// on; the LocalDB connection in appsettings.Development.json is SQL
/// Server too). Idempotent: guarded by sys.indexes.
/// </summary>
public sealed class AddIdentityLinkUniqueness(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string UserIdIndexName = "UX_ProgrammeOps_ExternalIdentityLink_source_userId";
    public const string EmailIndexName = "UX_ProgrammeOps_ExternalIdentityLink_source_email";

    protected override Task MigrateAsync()
    {
        var table = ExternalIdentityLinkDto.TableName;

        var duplicateUserIds = Database.Execute(
            $"DELETE l FROM [{table}] l WHERE l.[externalUserId] IS NOT NULL AND l.[id] > " +
            $"(SELECT MIN(k.[id]) FROM [{table}] k WHERE k.[externalSource] = l.[externalSource] AND k.[externalUserId] = l.[externalUserId])");
        var duplicateEmails = Database.Execute(
            $"DELETE l FROM [{table}] l WHERE l.[email] IS NOT NULL AND l.[id] > " +
            $"(SELECT MIN(k.[id]) FROM [{table}] k WHERE k.[externalSource] = l.[externalSource] AND k.[email] = l.[email])");

        if (duplicateUserIds + duplicateEmails > 0)
        {
            Database.Insert(new AuditLogDto
            {
                LogKey = Guid.NewGuid(),
                EntityType = "Migration",
                EntityId = nameof(AddIdentityLinkUniqueness),
                Action = "DuplicateIdentityLinksRemoved",
                ActorMemberId = null,
                DetailJson = JsonSerializer.Serialize(new { duplicateUserIds, duplicateEmails }),
                TimestampUtc = DateTime.UtcNow
            });
        }

        CreateFilteredUniqueIndex(UserIdIndexName, table, "externalUserId");
        CreateFilteredUniqueIndex(EmailIndexName, table, "email");

        return Task.CompletedTask;
    }

    private void CreateFilteredUniqueIndex(string indexName, string table, string column)
    {
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{indexName}] ON [{table}] ([externalSource], [{column}]) WHERE [{column}] IS NOT NULL");
    }
}
