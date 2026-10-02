using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Creates the ContractOps tables. Idempotent via DoesTableExist checks,
/// same as every other feature area's first migration.
/// </summary>
public sealed class AddContractOpsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, ContractDto.TableName))
        {
            Create.Table<ContractDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ContractDocumentDto.TableName))
        {
            Create.Table<ContractDocumentDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, ContractAuditLogDto.TableName))
        {
            Create.Table<ContractAuditLogDto>().Do();
        }

        return Task.CompletedTask;
    }
}
