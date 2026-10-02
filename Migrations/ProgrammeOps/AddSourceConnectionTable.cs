using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Creates ProgrammeOps_SourceConnection — the structural half of the GTM
/// review's B03 item (see Models/Programme/SourceConnection.cs for what it
/// deliberately does and does not do). A filtered unique index enforces at
/// most one *active* connection per (tenantId, source); a tenant can still
/// accumulate inactive/historical rows without violating it.
/// </summary>
public sealed class AddSourceConnectionTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string ActiveTenantSourceIndexName = "UX_ProgrammeOps_SourceConnection_active_tenant_source";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, SourceConnectionDto.TableName))
        {
            Create.Table<SourceConnectionDto>().Do();
        }

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{ActiveTenantSourceIndexName}' AND object_id = OBJECT_ID('[{SourceConnectionDto.TableName}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{ActiveTenantSourceIndexName}] ON [{SourceConnectionDto.TableName}] ([tenantId], [source]) WHERE [isActive] = 1");

        // Provenance column on ExternalIdentityLink — see ExternalIdentityLink.ConnectionKey's
        // doc comment. Nullable, no backfill: existing links predate connections
        // and simply have no ConnectionKey until re-linked.
        if (!ColumnExists(ExternalIdentityLinkDto.TableName, "connectionKey"))
        {
            Alter.Table(ExternalIdentityLinkDto.TableName)
                .AddColumn("connectionKey").AsGuid().Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
