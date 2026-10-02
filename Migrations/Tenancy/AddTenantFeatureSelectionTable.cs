using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

/// <summary>
/// Stores tenant-specific sellable add-ons on top of the plan defaults. The
/// plan remains the coarse commercial tier; this table is the per-tenant
/// module switch that turns the codebase's feature keys into something a
/// customer can actually buy selectively.
/// </summary>
public sealed class AddTenantFeatureSelectionTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string TenantFeatureIndexName = "UX_Tenancy_TenantFeatureSelection_tenant_feature";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, TenantFeatureSelectionDto.TableName))
        {
            Create.Table<TenantFeatureSelectionDto>().Do();
        }

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{TenantFeatureIndexName}' AND object_id = OBJECT_ID('[{TenantFeatureSelectionDto.TableName}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{TenantFeatureIndexName}] ON [{TenantFeatureSelectionDto.TableName}] ([tenantId], [featureKey])");

        return Task.CompletedTask;
    }
}
