using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Phase 2 of docs/architecture-review-2026-09-24.md: every tenant read on the
/// hot ProgrammeOps tables had no index beginning with tenantId, so each one
/// scanned every tenant's rows. These put tenantId first, then the column the
/// bounded reads seek or group on.
///
/// The TimeEntry (tenantId, workDate) index covers every column the DTO reads,
/// so a period report is one range seek with no lookups. It depends on
/// workDate holding the entry's ReportDate, which UpsertTimeEntryAsync writes
/// on every save; the backfill below restores that for any row inserted
/// another way since AddTimeEntryWorkDate ran. Idempotent: each index is
/// created only if absent.
/// </summary>
public sealed class AddTenantReadIndexes(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        Database.Execute(
            $"UPDATE {TimeEntryDto.TableName} SET workDate = CONVERT(date, startedAtUtc) WHERE workDate IS NULL AND startedAtUtc IS NOT NULL");

        var timeEntry = TimeEntryDto.TableName;
        CreateIndex("IX_ProgrammeOps_TimeEntry_tenant_workDate", timeEntry, "[tenantId], [workDate]",
            "[timeEntryKey], [workItemKey], [staffKey], [durationHours], [startedAtUtc], [isBillable], [billabilityKnown], [externalSource], [externalId], [createdAtUtc], [updatedAtUtc]");
        CreateIndex("IX_ProgrammeOps_TimeEntry_tenant_workItem", timeEntry, "[tenantId], [workItemKey]", "[durationHours]");
        CreateIndex("IX_ProgrammeOps_TimeEntry_tenant_staff_workItem", timeEntry, "[tenantId], [staffKey], [workItemKey]", "[durationHours]");
        CreateIndex("IX_ProgrammeOps_TimeEntry_tenant_source", timeEntry, "[tenantId], [externalSource]");

        CreateIndex("IX_ProgrammeOps_WorkItem_tenant_workstream", WorkItemDto.TableName, "[tenantId], [workstreamKey]");
        CreateIndex("IX_ProgrammeOps_Workstream_tenant", WorkstreamDto.TableName, "[tenantId]");
        CreateIndex("IX_ProgrammeOps_Project_tenant", ProjectDto.TableName, "[tenantId]");
        CreateIndex("IX_ProgrammeOps_Programme_tenant", ProgrammeDto.TableName, "[tenantId]");
        CreateIndex("IX_ProgrammeOps_WorkItemAllocation_tenant_workItem", WorkItemAllocationDto.TableName, "[tenantId], [workItemKey]");
        CreateIndex("IX_ProgrammeOps_PlannedAllocation_tenant_start", PlannedAllocationDto.TableName, "[tenantId], [startUtc]");
        CreateIndex("IX_ProgrammeOps_Alert_tenant_type", AlertDto.TableName, "[tenantId], [type], [entityKey]");
        CreateIndex("IX_ProgrammeOps_AuditLog_tenant_timestamp", AuditLogDto.TableName, "[tenantId], [timestampUtc]");
        return Task.CompletedTask;
    }

    private void CreateIndex(string indexName, string table, string keyColumns, string? includeColumns = null)
    {
        var include = includeColumns is null ? string.Empty : $" INCLUDE ({includeColumns})";
        Database.Execute(
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{table}]')) " +
            $"CREATE NONCLUSTERED INDEX [{indexName}] ON [{table}] ({keyColumns}){include}");
    }
}
