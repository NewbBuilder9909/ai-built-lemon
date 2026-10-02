using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Adds the column a tenant's own encrypted ClickUp/Hub Planner credential
/// lives in (see Services/ProgrammeOps/ISourceCredentialProtector) — nullable,
/// no backfill: an absent value means "use the deployment-wide
/// ClickUp:ApiToken / HubPlanner:ApiKey fallback", not an error. See
/// docs/programme-ops.md's "Tenant-owned source connections" section.
/// </summary>
public sealed class AddSourceConnectionCredentials(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(SourceConnectionDto.TableName, "protectedCredentialJson"))
        {
            Alter.Table(SourceConnectionDto.TableName)
                .AddColumn("protectedCredentialJson").AsString(2000).Nullable()
                .Do();
        }

        return Task.CompletedTask;
    }
}
