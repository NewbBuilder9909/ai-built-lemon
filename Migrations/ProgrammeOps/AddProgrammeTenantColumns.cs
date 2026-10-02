using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Tenancy;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Adds a nullable tenantId to all 23 ProgrammeOps_* tables and backfills
/// every existing row to the one default tenant, extending the tenant
/// isolation piloted on Branding Ops (and already applied to Staff Ops) to
/// Programme Ops — see docs/tenancy.md. This is the schema half of the GTM
/// review's B02 item; repositories, controllers and query services follow
/// in a later change. New step rather than editing a previous ProgrammeOps
/// migration in place, per the append-only convention.
///
/// Also re-keys the two ExternalIdentityLink uniqueness indexes added by
/// AddIdentityLinkUniqueness to be tenant-qualified (tenantId prepended to
/// each key) — the tenantId column those indexes need doesn't exist until
/// this step runs, so the old, non-tenant-qualified indexes are dropped and
/// replaced here rather than edited in place in AddIdentityLinkUniqueness.
/// </summary>
public sealed class AddProgrammeTenantColumns(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string TenantUserIdIndexName = "UX_ProgrammeOps_ExternalIdentityLink_tenant_source_userId";
    public const string TenantEmailIndexName = "UX_ProgrammeOps_ExternalIdentityLink_tenant_source_email";

    protected override Task MigrateAsync()
    {
        AddTenantColumnAndBackfill(ProgrammeDto.TableName);
        AddTenantColumnAndBackfill(ProjectDto.TableName);
        AddTenantColumnAndBackfill(WorkstreamDto.TableName);
        AddTenantColumnAndBackfill(WorkItemDto.TableName);
        AddTenantColumnAndBackfill(DependencyDto.TableName);
        AddTenantColumnAndBackfill(RiskDto.TableName);
        AddTenantColumnAndBackfill(IssueDto.TableName);
        AddTenantColumnAndBackfill(AuditLogDto.TableName);
        AddTenantColumnAndBackfill(RawClickUpPayloadDto.TableName);
        AddTenantColumnAndBackfill(TimeEntryDto.TableName);
        AddTenantColumnAndBackfill(CustomerDto.TableName);
        AddTenantColumnAndBackfill(WorkstreamBaselineDto.TableName);
        AddTenantColumnAndBackfill(ChangeRequestDto.TableName);
        AddTenantColumnAndBackfill(ProgrammeStakeholderDto.TableName);
        AddTenantColumnAndBackfill(ReportingSnapshotDto.TableName);
        AddTenantColumnAndBackfill(AlertDto.TableName);
        AddTenantColumnAndBackfill(WorkItemAllocationDto.TableName);
        AddTenantColumnAndBackfill(RawHubPlannerPayloadDto.TableName);
        AddTenantColumnAndBackfill(PlannedAllocationDto.TableName);
        AddTenantColumnAndBackfill(ExternalIdentityLinkDto.TableName);
        AddTenantColumnAndBackfill(UnresolvedIdentityDto.TableName);
        AddTenantColumnAndBackfill(SyncRunDto.TableName);
        AddTenantColumnAndBackfill(SyncLeaseDto.TableName);

        RetagIdentityLinkUniquenessIndexes();

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

    private void RetagIdentityLinkUniquenessIndexes()
    {
        var table = ExternalIdentityLinkDto.TableName;

        DropIndexIfExists(AddIdentityLinkUniqueness.UserIdIndexName, table);
        DropIndexIfExists(AddIdentityLinkUniqueness.EmailIndexName, table);

        CreateTenantFilteredUniqueIndex(TenantUserIdIndexName, table, "externalUserId");
        CreateTenantFilteredUniqueIndex(TenantEmailIndexName, table, "email");
    }

    private void DropIndexIfExists(string indexName, string table)
    {
        Database.Execute(
            $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"DROP INDEX [{indexName}] ON [{table}]");
    }

    private void CreateTenantFilteredUniqueIndex(string indexName, string table, string column)
    {
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE UNIQUE NONCLUSTERED INDEX [{indexName}] ON [{table}] ([tenantId], [externalSource], [{column}]) WHERE [{column}] IS NOT NULL");
    }
}
