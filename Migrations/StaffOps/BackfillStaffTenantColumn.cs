using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Re-runs AddStaffTenantColumn's backfill. Between that migration and
/// StaffOnboardingService learning to stamp TenantId on creation, any staff
/// row created through /staffops/admin/create was left with a null tenant —
/// which TenantContextAccessor treats as "unresolved", locking that member
/// out of every tenant-scoped page. Idempotent: only touches null rows.
/// </summary>
public sealed class BackfillStaffTenantColumn(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        Database.Execute(
            $"UPDATE {StaffDto.TableName} SET tenantId = @0 WHERE tenantId IS NULL",
            Tenant.DefaultTenantKey);

        return Task.CompletedTask;
    }
}
