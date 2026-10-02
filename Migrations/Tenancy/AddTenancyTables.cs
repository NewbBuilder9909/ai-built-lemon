using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

/// <summary>
/// Creates the Tenancy_Tenant table and idempotently seeds the single
/// default tenant every existing (pre-tenancy) row across other feature
/// areas is backfilled to. Table creation follows the same
/// DoesTableExist-guarded pattern as Migrations/BrandingOps/AddBrandingOpsTables;
/// the seed check is a separate idempotency guard alongside it.
/// </summary>
public sealed class AddTenancyTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, TenantDto.TableName))
        {
            Create.Table<TenantDto>().Do();
        }

        var existing = Database.FirstOrDefault<TenantDto>(
            NPoco.Sql.Builder.Where("tenantKey = @0", Tenant.DefaultTenantKey));

        if (existing is null)
        {
            Database.Insert(new TenantDto
            {
                TenantKey = Tenant.DefaultTenantKey,
                Name = "Default",
                ShortCode = "default",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return Task.CompletedTask;
    }
}
