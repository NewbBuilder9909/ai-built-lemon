using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Adds StaffOps_LeaveRequest.tenantId and backfills every existing row to
/// the one default tenant (see Models/Tenancy/Tenant.DefaultTenantKey) —
/// extends the tenant isolation already applied to StaffOps_Staff
/// (AddStaffTenantColumn) to the leave approval queue, which today leaks
/// every tenant's pending requests into one Holiday Approver's view. New
/// step rather than editing an earlier StaffOps migration in place, per the
/// append-only convention; same nullable-then-backfill shape as
/// AddStaffTenantColumn.
/// </summary>
public sealed class AddLeaveRequestTenantColumn(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(LeaveRequestDto.TableName, "tenantId"))
        {
            Alter.Table(LeaveRequestDto.TableName)
                .AddColumn("tenantId").AsGuid().Nullable()
                .Do();
        }

        Database.Execute(
            $"UPDATE {LeaveRequestDto.TableName} SET tenantId = @0 WHERE tenantId IS NULL",
            Tenant.DefaultTenantKey);

        return Task.CompletedTask;
    }
}
