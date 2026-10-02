using ProgrammePulse.Tests.ProgrammeOps;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Integrations;

/// <summary>
/// The lease/run lifecycle the sync services rely on: acquisition across
/// instances, stale-run takeover, heartbeat throttling, and "dispose without
/// finishing marks the run failed". Backed by FakeSyncRunRepository, which
/// mirrors the SQL semantics of SyncRunRepository.
/// </summary>
public class SyncRunCoordinatorTests
{
    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static SyncRunCoordinator Build(FakeSyncRunRepository runs, SyncRunGuard? guard = null, TimeProvider? time = null, int leaseSeconds = 300) =>
        new(guard ?? new SyncRunGuard(), runs, Options.Create(new ProgrammeOpsOptions { SyncLeaseSeconds = leaseSeconds }), time ?? new MutableTimeProvider(Start));

    [Fact]
    public async Task Begin_takes_the_local_latch_and_the_database_lease_and_records_a_running_run()
    {
        var runs = new FakeSyncRunRepository();
        var guard = new SyncRunGuard();

        await using var handle = await Build(runs, guard).TryBeginAsync(TestTenantId, "ClickUp", 3);

        Assert.NotNull(handle);
        Assert.True(guard.IsRunning(TestTenantId, "ClickUp"));
        Assert.Equal(SyncRunCoordinator.InstanceId, runs.Leases[(TestTenantId, "ClickUp")].Owner);
        var run = runs.Single("ClickUp");
        Assert.Equal(SyncRunStatus.Running, run.Status);
        Assert.Equal(3, run.TriggeredByMemberId);
        Assert.Equal(SyncRunCoordinator.InstanceId, run.InstanceId);
    }

    [Fact]
    public async Task Begin_is_refused_when_another_instance_holds_a_live_lease_and_the_local_latch_is_released_again()
    {
        var runs = new FakeSyncRunRepository();
        runs.Leases[(TestTenantId, "ClickUp")] = ("elsewhere", Start.UtcDateTime.AddMinutes(2), Guid.NewGuid());
        var guard = new SyncRunGuard();

        var handle = await Build(runs, guard).TryBeginAsync(TestTenantId, "ClickUp", null);

        Assert.Null(handle);
        Assert.False(guard.IsRunning(TestTenantId, "ClickUp"));
        Assert.Empty(runs.Runs);
    }

    [Fact]
    public async Task Begin_takes_over_an_expired_lease_and_marks_the_abandoned_run_failed()
    {
        var runs = new FakeSyncRunRepository();
        var abandoned = await runs.TryAcquireAsync(TestTenantId, "ClickUp", "crashed-instance", null, Start.UtcDateTime.AddMinutes(-10), TimeSpan.FromMinutes(5));

        await using var handle = await Build(runs).TryBeginAsync(TestTenantId, "ClickUp", null);

        Assert.NotNull(handle);
        var old = runs.Runs.Single(r => r.RunKey == abandoned!.RunKey);
        Assert.Equal(SyncRunStatus.Failed, old.Status);
        Assert.Equal(SyncRunRepository.AbandonedError, old.Error);
        Assert.Equal(SyncRunCoordinator.InstanceId, runs.Leases[(TestTenantId, "ClickUp")].Owner);
    }

    [Fact]
    public async Task Begin_is_refused_locally_when_this_process_already_has_the_source_in_flight()
    {
        var runs = new FakeSyncRunRepository();
        var guard = new SyncRunGuard();
        using var inFlight = guard.TryEnter(TestTenantId, "ClickUp");

        var handle = await Build(runs, guard).TryBeginAsync(TestTenantId, "ClickUp", null);

        Assert.Null(handle);
        Assert.Empty(runs.Runs);
    }

    [Fact]
    public async Task ReportStage_heartbeats_every_time_but_Touch_only_after_a_third_of_the_lease()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Start);
        await using var handle = (await Build(runs, time: time, leaseSeconds: 300).TryBeginAsync(TestTenantId, "ClickUp", null))!;

        await handle.ReportStageAsync("fetching spaces");
        await handle.TouchAsync();
        time.Now = Start.AddSeconds(50);
        await handle.TouchAsync();
        time.Now = Start.AddSeconds(101);
        await handle.TouchAsync();

        Assert.Equal(2, runs.HeartbeatCount);
        Assert.Equal("fetching spaces", runs.Single("ClickUp").Stage);
        Assert.Equal(Start.AddSeconds(101).UtcDateTime, runs.Single("ClickUp").HeartbeatAtUtc);
    }

    [Fact]
    public async Task Complete_marks_the_run_succeeded_with_the_summary_and_releases_both_leases()
    {
        var runs = new FakeSyncRunRepository();
        var guard = new SyncRunGuard();
        var handle = (await Build(runs, guard).TryBeginAsync(TestTenantId, "ClickUp", null))!;

        await handle.CompleteAsync("1 programme", new { programmes = 1 });
        await handle.DisposeAsync();

        var run = runs.Single("ClickUp");
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal("1 programme", run.Summary);
        Assert.Contains("\"programmes\":1", run.SummaryJson);
        Assert.Null(runs.Leases[(TestTenantId, "ClickUp")].Owner);
        Assert.False(guard.IsRunning(TestTenantId, "ClickUp"));
    }

    // ---- Lease-loss fencing: a worker that stalled past its lease and was
    // taken over must stop, must not publish, and must not touch the new
    // owner's lease or its own abandoned-run marker. ----

    private static async Task<(SyncRunHandle Stalled, SyncRun TakenOverBy)> StallAndTakeOverAsync(FakeSyncRunRepository runs, MutableTimeProvider time, SyncRunGuard? guard = null)
    {
        var stalled = (await Build(runs, guard, time, leaseSeconds: 300).TryBeginAsync(TestTenantId, "ClickUp", null))!;
        await stalled.ReportStageAsync("syncing tasks");

        // The worker pauses for longer than the whole lease; another
        // instance acquires the expired lease and marks the run abandoned.
        time.Now = Start.AddMinutes(6);
        var takeover = (await runs.TryAcquireAsync(TestTenantId, "ClickUp", "other-instance", null, time.Now.UtcDateTime, TimeSpan.FromMinutes(5)))!;
        return (stalled, takeover);
    }

    [Fact]
    public async Task A_touch_after_the_lease_was_taken_over_throws_and_the_handle_stays_lost()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Start);
        var (stalled, takeover) = await StallAndTakeOverAsync(runs, time);

        var ex = await Assert.ThrowsAsync<SyncLeaseLostException>(stalled.TouchAsync);
        Assert.Equal(stalled.Run.RunKey, ex.RunKey);
        Assert.True(stalled.LeaseLost);
        Assert.Equal(1, runs.RefusedHeartbeats);

        // Every later call fails fast without another database round-trip.
        await Assert.ThrowsAsync<SyncLeaseLostException>(stalled.TouchAsync);
        await Assert.ThrowsAsync<SyncLeaseLostException>(() => stalled.ReportStageAsync("syncing time entries"));
        Assert.Equal(1, runs.RefusedHeartbeats);

        Assert.Equal("other-instance", runs.Leases[(TestTenantId, "ClickUp")].Owner);
        Assert.Equal(takeover.RunKey, runs.Leases[(TestTenantId, "ClickUp")].RunKey);
        await stalled.DisposeAsync();
    }

    [Fact]
    public async Task A_completion_after_the_lease_was_taken_over_is_refused_and_the_abandoned_marker_survives()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Start);
        var (stalled, takeover) = await StallAndTakeOverAsync(runs, time);

        await Assert.ThrowsAsync<SyncLeaseLostException>(() => stalled.CompleteAsync("done", new { }));
        await stalled.DisposeAsync();

        Assert.Equal(1, runs.RefusedCompletions);
        var old = runs.Runs.Single(r => r.RunKey == stalled.Run.RunKey);
        Assert.Equal(SyncRunStatus.Failed, old.Status);
        Assert.Equal(SyncRunRepository.AbandonedError, old.Error);
        Assert.Null(old.Summary);
        var live = runs.Runs.Single(r => r.RunKey == takeover.RunKey);
        Assert.Equal(SyncRunStatus.Running, live.Status);
        Assert.Equal("other-instance", runs.Leases[(TestTenantId, "ClickUp")].Owner);
    }

    [Fact]
    public async Task Failing_or_disposing_a_taken_over_run_neither_overwrites_the_abandoned_marker_nor_releases_the_new_owners_lease()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Start);
        var guard = new SyncRunGuard();
        var (stalled, takeover) = await StallAndTakeOverAsync(runs, time, guard);

        await stalled.FailAsync(new HttpRequestException("late failure from the stalled worker"));
        await stalled.DisposeAsync();

        var old = runs.Runs.Single(r => r.RunKey == stalled.Run.RunKey);
        Assert.Equal(SyncRunRepository.AbandonedError, old.Error);
        Assert.Equal("other-instance", runs.Leases[(TestTenantId, "ClickUp")].Owner);
        Assert.Equal(takeover.RunKey, runs.Leases[(TestTenantId, "ClickUp")].RunKey);
        Assert.False(guard.IsRunning(TestTenantId, "ClickUp"));
    }

    [Fact]
    public async Task A_live_worker_that_merely_ran_long_still_completes_because_its_heartbeats_kept_the_lease()
    {
        var runs = new FakeSyncRunRepository();
        var time = new MutableTimeProvider(Start);
        var handle = (await Build(runs, time: time, leaseSeconds: 300).TryBeginAsync(TestTenantId, "ClickUp", null))!;

        for (var minute = 2; minute <= 20; minute += 2)
        {
            time.Now = Start.AddMinutes(minute);
            await handle.TouchAsync();
        }

        Assert.Null(await runs.TryAcquireAsync(TestTenantId, "ClickUp", "other-instance", null, time.Now.UtcDateTime, TimeSpan.FromMinutes(5)));
        await handle.CompleteAsync("ok", new { });
        await handle.DisposeAsync();

        Assert.Equal(SyncRunStatus.Succeeded, runs.Single("ClickUp").Status);
        Assert.Equal(0, runs.RefusedHeartbeats + runs.RefusedCompletions);
    }

    [Fact]
    public async Task Disposing_without_finishing_marks_the_run_failed_rather_than_leaving_it_running()
    {
        var runs = new FakeSyncRunRepository();
        var guard = new SyncRunGuard();
        var handle = (await Build(runs, guard).TryBeginAsync(TestTenantId, "ClickUp", null))!;
        await handle.ReportStageAsync("syncing tasks");

        await handle.DisposeAsync();

        var run = runs.Single("ClickUp");
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal("syncing tasks", run.Stage);
        Assert.Contains("without completing", run.Error);
        Assert.Null(runs.Leases[(TestTenantId, "ClickUp")].Owner);
        Assert.False(guard.IsRunning(TestTenantId, "ClickUp"));
    }
}
