using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.BrandingOps;

/// <summary>
/// Creates the BrandingOps tables. Idempotent via DoesTableExist checks, same
/// pattern as Migrations/ProgrammeOps/AddProgrammeOpsTables.
/// </summary>
public sealed class AddBrandingOpsTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, BrandingProfileDto.TableName))
        {
            Create.Table<BrandingProfileDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, BrandingAssetDto.TableName))
        {
            Create.Table<BrandingAssetDto>().Do();
        }

        if (!SqlSyntax.DoesTableExist(Database, BrandingAuditLogDto.TableName))
        {
            Create.Table<BrandingAuditLogDto>().Do();
        }

        return Task.CompletedTask;
    }
}
