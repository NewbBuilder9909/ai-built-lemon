using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds StaffOps_Staff.tenantId and backfills every existing row to the one
/// default tenant (see Models/Tenancy/Tenant.DefaultTenantKey) — the anchor
/// for Services/Tenancy/TenantContextAccessor's member-based resolution.
/// New step rather than editing AddStaffOpsTables in place, per the
/// append-only convention; same nullable-then-backfill shape as
/// Migrations/ProgrammeOps/AddProgrammeBudgetColumns.
/// </summary>
public sealed class AddStaffTenantColumn(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(StaffDto.TableName, "tenantId"))
        {
            Alter.Table(StaffDto.TableName)
                .AddColumn("tenantId").AsGuid().Nullable()
                .Do();
        }

        Database.Execute(
            $"UPDATE {StaffDto.TableName} SET tenantId = @0 WHERE tenantId IS NULL",
            Tenant.DefaultTenantKey);

        return Task.CompletedTask;
    }
}
