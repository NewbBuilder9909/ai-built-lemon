using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Step 1 of docs/delivery-evidence-and-contract-assurance.md: declared
/// repository → project links. A new table, so tenantId is non-nullable
/// from the start (there are no legacy rows to accommodate).
///
/// UX_ProgrammeOps_CodeRepositoryLink_live is the load-bearing index: one
/// live link per (tenant, repository, project), while every ended link is
/// kept. Idempotent.
/// </summary>
public sealed class AddCodeRepositoryLinkTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string LiveIndexName = "UX_ProgrammeOps_CodeRepositoryLink_live";

    protected override Task MigrateAsync()
    {
        var table = CodeRepositoryLinkDto.TableName;
        if (!SqlSyntax.DoesTableExist(Database, table))
        {
            Create.Table<CodeRepositoryLinkDto>().Do();
        }

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{LiveIndexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{LiveIndexName}] ON [{table}] ([tenantId], [provider], [sourceAccountId], [repositoryKey], [projectKey]) WHERE [removedAtUtc] IS NULL");
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProgrammeOps_CodeRepositoryLink_tenant_project' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE NONCLUSTERED INDEX [IX_ProgrammeOps_CodeRepositoryLink_tenant_project] ON [{table}] ([tenantId], [projectKey])");
        return Task.CompletedTask;
    }
}
