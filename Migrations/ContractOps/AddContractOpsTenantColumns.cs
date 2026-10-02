using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ContractOps;

/// <summary>
/// Adds a nullable tenantId to all 6 ContractOps_* tables and backfills
/// every existing row to the one default tenant, extending the tenant
/// isolation piloted on Branding Ops (and already applied to Staff Ops and
/// Programme Ops) to Contract Ops — see docs/tenancy.md. New step rather
/// than editing an earlier ContractOps migration in place, per the
/// append-only convention; same nullable-then-backfill shape as
/// Migrations/ProgrammeOps/AddProgrammeTenantColumns.
/// </summary>
public sealed class AddContractOpsTenantColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        AddTenantColumnAndBackfill(ContractDto.TableName);
        AddTenantColumnAndBackfill(ContractDocumentDto.TableName);
        AddTenantColumnAndBackfill(NonLabourCostDto.TableName);
        AddTenantColumnAndBackfill(InvoiceDto.TableName);
        AddTenantColumnAndBackfill(InvoiceLineDto.TableName);
        AddTenantColumnAndBackfill(ContractAuditLogDto.TableName);

        return Task.CompletedTask;
    }

    private void AddTenantColumnAndBackfill(string tableName)
    {
        if (!ColumnExists(tableName, "tenantId"))
        {
            Alter.Table(tableName)
                .AddColumn("tenantId").AsGuid().Nullable()
                .Do();
        }

        Database.Execute(
            $"UPDATE {tableName} SET tenantId = @0 WHERE tenantId IS NULL",
            Tenant.DefaultTenantKey);
    }
}
