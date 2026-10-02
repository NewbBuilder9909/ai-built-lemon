using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Adds non-labour cost capture (expenses, POs, vendor invoices) against a
/// Contract — new step rather than editing AddContractOpsTables in place,
/// per the append-only migration convention.
/// </summary>
public sealed class AddNonLabourCostTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, NonLabourCostDto.TableName))
        {
            Create.Table<NonLabourCostDto>().Do();
        }

        return Task.CompletedTask;
    }
}
