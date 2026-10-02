using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.Tenancy;

/// <summary>
/// Which benched modules a tenant's Admin has switched on (see
/// Models/Tenancy/ProductModule). Created empty: every module starts off
/// for every tenant, including those that existed before the bench.
/// </summary>
public sealed class AddTenantModuleSwitchTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string TenantModuleIndexName = "UX_Tenancy_ModuleSwitch_tenant_module";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, TenantModuleSwitchDto.TableName))
        {
            Create.Table<TenantModuleSwitchDto>().Do();
        }

        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{TenantModuleIndexName}' AND object_id = OBJECT_ID('[{TenantModuleSwitchDto.TableName}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{TenantModuleIndexName}] ON [{TenantModuleSwitchDto.TableName}] ([tenantId], [moduleKey])");

        return Task.CompletedTask;
    }
}
