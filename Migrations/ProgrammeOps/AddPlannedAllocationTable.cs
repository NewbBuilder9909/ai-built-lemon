using System.Text.Json;
using ProgrammePulse.Data.Dtos;
using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Creates ProgrammeOps_PlannedAllocation (see Models/Programme/PlannedAllocation)
/// and removes the Silver rows the previous Hub Planner mapping produced —
/// one WorkItem per booking under a synthetic "Bookings" Workstream, marked
/// Done once the booking's end date passed. Those rows overstated delivery
/// progress in every Gold consumer that counts Done items, which is the
/// reason this entity exists; leaving them in place would keep the defect
/// live until the next sync happened to run. They are derived data (fully
/// re-creatable from Bronze / a fresh sync), the integration has never run
/// against a live account (docs/release-readiness.md R21), and the
/// deletion is recorded in ProgrammeOps_AuditLog with the row counts so it
/// is never a silent migration side effect. Idempotent: the DDL is guarded
/// by DoesTableExist and the DELETEs match nothing on a second run.
/// </summary>
public sealed class AddPlannedAllocationTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    public const string LegacySource = "HubPlanner";

    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, PlannedAllocationDto.TableName))
        {
            Create.Table<PlannedAllocationDto>().Do();
        }

        var legacyWorkItems = $"SELECT [workItemKey] FROM [{WorkItemDto.TableName}] WHERE [externalSource] = @0";
        var legacyWorkstreams = $"SELECT [workstreamKey] FROM [{WorkstreamDto.TableName}] WHERE [externalSource] = @0";

        var allocations = Database.Execute(
            $"DELETE FROM [{WorkItemAllocationDto.TableName}] WHERE [workItemKey] IN ({legacyWorkItems})", LegacySource);
        var dependencies = Database.Execute(
            $"DELETE FROM [{DependencyDto.TableName}] WHERE [workItemKey] IN ({legacyWorkItems}) OR [dependsOnWorkItemKey] IN ({legacyWorkItems})", LegacySource);
        var workItems = Database.Execute(
            $"DELETE FROM [{WorkItemDto.TableName}] WHERE [externalSource] = @0", LegacySource);
        var baselines = Database.Execute(
            $"DELETE FROM [{WorkstreamBaselineDto.TableName}] WHERE [workstreamKey] IN ({legacyWorkstreams})", LegacySource);
        var workstreams = Database.Execute(
            $"DELETE FROM [{WorkstreamDto.TableName}] WHERE [externalSource] = @0", LegacySource);

        if (allocations + dependencies + workItems + baselines + workstreams > 0)
        {
            Database.Insert(new AuditLogDto
            {
                LogKey = Guid.NewGuid(),
                EntityType = "Migration",
                EntityId = nameof(AddPlannedAllocationTable),
                Action = "LegacyHubPlannerWorkItemsRemoved",
                ActorMemberId = null,
                DetailJson = JsonSerializer.Serialize(new { workItems, allocations, dependencies, workstreams, baselines }),
                TimestampUtc = DateTime.UtcNow
            });
        }

        return Task.CompletedTask;
    }
}
