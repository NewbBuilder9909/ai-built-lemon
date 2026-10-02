using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Audit trail for StaffAdminController's sensitive actions (staff creation,
/// rate changes, GDPR export/erasure) — StaffOps had none of this, unlike
/// ContractOps/BrandingOps. New step rather than editing AddStaffOpsTables in
/// place, per the append-only convention.
/// </summary>
public sealed class AddStaffAuditLogTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, StaffAuditLogDto.TableName))
        {
            Create.Table<StaffAuditLogDto>().Do();
        }

        return Task.CompletedTask;
    }
}
