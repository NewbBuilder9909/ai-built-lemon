using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.BrandingOps;

/// <summary>
/// Adds a nullable tenantId to all three BrandingOps tables and backfills
/// every existing row to the one default tenant, piloting real tenant data
/// isolation on Branding Ops (see docs/tenancy.md). New step rather than
/// editing AddBrandingOpsTables in place, per the append-only convention;
/// same nullable-then-backfill shape as
/// Migrations/ProgrammeOps/AddProgrammeBudgetColumns.
/// </summary>
public sealed class AddBrandingTenantColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        AddTenantColumnAndBackfill(BrandingProfileDto.TableName);
        AddTenantColumnAndBackfill(BrandingAssetDto.TableName);
        AddTenantColumnAndBackfill(BrandingAuditLogDto.TableName);

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
