using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Invoice header + line items — the presentation layer over the revenue
/// calculation ContractCommercialService already computes. New step rather
/// than editing earlier ones in place, per the append-only convention.
/// </summary>
public sealed class AddInvoiceTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, InvoiceDto.TableName))
        {
            Create.Table<InvoiceDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, InvoiceLineDto.TableName))
        {
            Create.Table<InvoiceLineDto>().Do();
        }

        return Task.CompletedTask;
    }
}
