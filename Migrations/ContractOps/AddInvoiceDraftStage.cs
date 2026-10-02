using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Decision 4 of docs/delivery-evidence-and-contract-assurance.md: invoices
/// gain a Draft stage. Adds <c>issuedAtUtc</c> (the invoice date, null while a
/// draft) and <c>needsReviewHours</c> (hours of unknown billability listed on
/// the draft). Every existing invoice was issued at generation, so its issue
/// date is backfilled from <c>createdAtUtc</c>. Status is stored by name, so
/// Draft and Discarded need no schema change. Idempotent.
/// </summary>
public sealed class AddInvoiceDraftStage(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(InvoiceDto.TableName, "issuedAtUtc"))
        {
            Database.Execute($"ALTER TABLE [{InvoiceDto.TableName}] ADD [issuedAtUtc] DATETIME2 NULL");
        }

        if (!ColumnExists(InvoiceDto.TableName, "needsReviewHours"))
        {
            Database.Execute(
                $"ALTER TABLE [{InvoiceDto.TableName}] ADD [needsReviewHours] DECIMAL(18, 2) NOT NULL " +
                "CONSTRAINT [DF_ContractOps_Invoice_needsReviewHours] DEFAULT 0");
        }

        Database.Execute(
            $"UPDATE [{InvoiceDto.TableName}] SET [issuedAtUtc] = [createdAtUtc] " +
            "WHERE [issuedAtUtc] IS NULL AND [status] IN ('Issued', 'Paid')");

        return Task.CompletedTask;
    }
}
