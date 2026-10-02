using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

/// <summary>
/// Adds the commercial lifecycle columns to Tenancy_Tenant (status,
/// planName, trialEndsAtUtc, updatedAtUtc) and backfills every existing row
/// so nothing that works today stops working: an active row becomes
/// Status=Active, an inactive one Suspended, and every existing tenant is
/// put on the Enterprise plan (all features) — downgrading a live customer
/// is a deliberate platform-admin action, never a migration side effect.
/// Nullable-then-backfill, same shape as AddStaffTenantColumn.
///
/// The column is "planName" rather than "plan" because PLAN is a reserved
/// word in T-SQL — the first cut of this migration failed on exactly that
/// in the hand-written UPDATE below, which is why the identifiers here are
/// bracketed as well.
/// </summary>
public sealed class AddTenantLifecycleColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(TenantDto.TableName, "status"))
        {
            Alter.Table(TenantDto.TableName).AddColumn("status").AsInt32().Nullable().Do();
        }

        if (!ColumnExists(TenantDto.TableName, "planName"))
        {
            Alter.Table(TenantDto.TableName).AddColumn("planName").AsString(32).Nullable().Do();
        }

        if (!ColumnExists(TenantDto.TableName, "trialEndsAtUtc"))
        {
            Alter.Table(TenantDto.TableName).AddColumn("trialEndsAtUtc").AsDateTime().Nullable().Do();
        }

        if (!ColumnExists(TenantDto.TableName, "updatedAtUtc"))
        {
            Alter.Table(TenantDto.TableName).AddColumn("updatedAtUtc").AsDateTime().Nullable().Do();
        }

        Database.Execute(
            $"UPDATE [{TenantDto.TableName}] SET [status] = CASE WHEN [isActive] = 1 THEN @0 ELSE @1 END WHERE [status] IS NULL",
            (int)TenantStatus.Active,
            (int)TenantStatus.Suspended);

        Database.Execute(
            $"UPDATE [{TenantDto.TableName}] SET [planName] = @0 WHERE [planName] IS NULL",
            TenantPlan.Enterprise);

        return Task.CompletedTask;
    }
}
