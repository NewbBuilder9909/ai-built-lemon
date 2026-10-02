using System.Text.Json;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public class ExecutivePackPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData(SyncRunStatus.Running)]
    [InlineData(SyncRunStatus.Failed)]
    public void Unfinished_or_failed_publications_cannot_be_captured(SyncRunStatus status) =>
        Assert.Throws<ReviewValidationException>(() => ExecutivePackPolicy.RequirePublication(Run() with { Status = status }, Now, 48));

    [Fact]
    public void Missing_stale_future_and_invalid_publications_are_rejected()
    {
        SyncRun?[] invalid = [null, Run() with { FinishedAtUtc = null },
            Run() with { StartedAtUtc = Now.AddHours(-51), FinishedAtUtc = Now.AddHours(-49) },
            Run() with { FinishedAtUtc = Now.AddMinutes(1) }, Run() with { StartedAtUtc = Now }];
        foreach (var run in invalid) Assert.Throws<ReviewValidationException>(() => ExecutivePackPolicy.RequirePublication(run, Now, 48));
        ExecutivePackPolicy.RequirePublication(Run(), Now, 48);
    }

    [Fact]
    public void Evidence_rejects_foreign_tenant_and_missing_source_ids()
    {
        Assert.Throws<ReviewValidationException>(() => ExecutivePackPolicy.Evidence([Item() with { TenantId = Guid.NewGuid() }], Tenant, "source"));
        Assert.Throws<ReviewValidationException>(() => ExecutivePackPolicy.Evidence([Item() with { ExternalId = null }], Tenant, "source"));
        Assert.Throws<ReviewValidationException>(() => ExecutivePackPolicy.Evidence([Item() with { ExternalSource = "other" }], Tenant, "source"));
    }

    [Fact]
    public void Frozen_evidence_preserves_unknowns_and_ignores_completed_overdue_items()
    {
        var items = ExecutivePackPolicy.Evidence([
            Item() with { Stage = WorkItemLifecycleStage.Blocked, DueDateUtc = Now.AddDays(-1) },
            Item() with { Stage = WorkItemLifecycleStage.Unmapped },
            Item() with { Stage = WorkItemLifecycleStage.Done, DueDateUtc = Now.AddDays(-1) },
            Item() with { Stage = WorkItemLifecycleStage.Cancelled, DueDateUtc = Now.AddDays(-1) }
        ], Tenant, "source");
        var pack = new ExecutivePack(Guid.NewGuid(), null, Now, "operational-evidence-v1", new(0, new(), null, null),
            "source", Guid.NewGuid(), "account", Guid.NewGuid(), Now.AddHours(-1), 48, items, ExecutivePackPolicy.Summarize(items, Now));
        var copy = JsonSerializer.Deserialize<ExecutivePack>(JsonSerializer.Serialize(pack))!;
        Assert.Equal(4, copy.Total);
        Assert.Equal(2, copy.Open);
        Assert.Equal(1, copy.Blocked);
        Assert.Equal(1, copy.Overdue);
        Assert.Equal(1, copy.OpenWithoutDueDate);
        Assert.Equal(1, copy.Unmapped);
        Assert.Equal(DateTimeKind.Utc, copy.CapturedAtUtc.Kind);
        Assert.DoesNotContain("sensitive title", JsonSerializer.Serialize(pack));
        Assert.DoesNotContain("AssignedStaff", JsonSerializer.Serialize(pack));
    }

    private static SyncRun Run() => new()
    {
        RunKey = Guid.NewGuid(), TenantId = Tenant, Source = "source", Status = SyncRunStatus.Succeeded,
        StartedAtUtc = Now.AddHours(-2), FinishedAtUtc = Now.AddHours(-1), HeartbeatAtUtc = Now.AddHours(-1), InstanceId = "test"
    };

    private static WorkItem Item() => new()
    {
        WorkItemKey = Guid.NewGuid(), WorkstreamKey = Guid.NewGuid(), TenantId = Tenant,
        Title = "sensitive title", Stage = WorkItemLifecycleStage.Ready, ExternalSource = "source", ExternalId = "item",
        CreatedAtUtc = Now.AddDays(-2), UpdatedAtUtc = Now.AddHours(-1)
    };
}
