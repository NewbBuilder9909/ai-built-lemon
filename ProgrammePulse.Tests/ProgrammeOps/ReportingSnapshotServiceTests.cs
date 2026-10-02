using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class ReportingSnapshotServiceTests
{
    private sealed class FakeAvailabilityRepository : IAvailabilityRepository
    {
        public Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<Availability>>([]);

        public Task<Availability> CreateAsync(Availability availability) => throw new NotSupportedException();
    }

    private sealed class FakeStaffRateRepository : IStaffRateRepository
    {
        public Task<StaffRate?> GetCurrentAsync(Guid staffKey) => Task.FromResult<StaffRate?>(null);
        public Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey) => Task.FromResult<IReadOnlyList<StaffRate>>([]);
        public Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey) => throw new NotSupportedException();
    }

    private sealed class FakeWorkHoursHistoryRepository : IWorkHoursHistoryRepository
    {
        public Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey) => Task.FromResult<WorkHoursHistory?>(null);
        public Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey) => Task.FromResult<IReadOnlyList<WorkHoursHistory>>([]);
        public Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ReportingSnapshotService BuildSut(FakeProgrammeRepository repository, FakeStaffRepository staff, DateTimeOffset? now = null) =>
        new(
            new ReportingQueryService(repository, staff, new FakeAvailabilityRepository(), new FakeStaffRateRepository(), new FakeWorkHoursHistoryRepository(), new ProgrammeOverviewQueryService(repository, staff, new FixedTimeProvider(now ?? Now)), new FixedTimeProvider(now ?? Now)),
            repository,
            new FixedTimeProvider(now ?? Now));

    private static (Programme Programme, Project Project, Workstream Workstream) SeedHierarchy(FakeProgrammeRepository repository)
    {
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Drama", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var project = new Project { ProjectKey = Guid.NewGuid(), TenantId = TestTenantId, ProgrammeKey = programme.ProgrammeKey, Name = "Series 4", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var workstream = new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = TestTenantId, ProjectKey = project.ProjectKey, Name = "Post", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };

        repository.Programmes.Add(programme);
        repository.Projects.Add(project);
        repository.Workstreams.Add(workstream);

        return (programme, project, workstream);
    }

    [Fact]
    public async Task CaptureAsync_sums_effort_variance_and_customer_rollup_totals_into_one_snapshot()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var doneItem = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "A", Stage = WorkItemLifecycleStage.Done, EstimatedHours = 5m, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var blockedItem = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "B", Stage = WorkItemLifecycleStage.Blocked, EstimatedHours = 3m, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        repository.WorkItems.Add(doneItem);
        repository.WorkItems.Add(blockedItem);
        repository.TimeEntries.Add(new TimeEntry { TimeEntryKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = doneItem.WorkItemKey, DurationHours = 6m, StartedAtUtc = Now.UtcDateTime, IsBillable = true, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime });

        var sut = BuildSut(repository, new FakeStaffRepository());
        var result = await sut.CaptureAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, TestTenantId);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        var snapshot = Assert.Single(repository.ReportingSnapshots);
        Assert.Equal(8m, snapshot.TotalEstimatedHours); // 5 + 3
        Assert.Equal(6m, snapshot.TotalActualHours);
        Assert.Equal(-2m, snapshot.TotalVarianceHours); // 6 - 8
        Assert.Equal(1, snapshot.OpenWorkItems); // the Blocked item (Done is closed)
        Assert.Equal(1, snapshot.BlockedWorkItems);
    }

    [Fact]
    public async Task GetHistoryAsync_returns_captured_snapshots_oldest_first()
    {
        var repository = new FakeProgrammeRepository();

        // Each period captured the morning after it ended.
        await BuildSut(repository, new FakeStaffRepository(), new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero))
            .CaptureAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), null, TestTenantId);
        await BuildSut(repository, new FakeStaffRepository())
            .CaptureAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, TestTenantId);

        var history = await BuildSut(repository, new FakeStaffRepository()).GetHistoryAsync(TestTenantId);

        Assert.Equal(2, history.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), history[0].PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 1), history[1].PeriodStart);
    }

    // A snapshot's figures are the day it is taken. Filed under a period that
    // ended long ago, September's open count would be reported as July's.
    [Fact]
    public async Task A_period_that_ended_more_than_a_week_ago_is_refused_and_nothing_is_stored()
    {
        var repository = new FakeProgrammeRepository();

        var result = await BuildSut(repository, new FakeStaffRepository())
            .CaptureAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), null, TestTenantId);

        Assert.Equal(CommandStatus.Refused, result.Status);
        Assert.Contains("more than 7 days ago", result.Message);
        Assert.Empty(repository.ReportingSnapshots);
    }

    [Fact]
    public async Task A_period_that_has_not_ended_is_refused()
    {
        var repository = new FakeProgrammeRepository();

        var result = await BuildSut(repository, new FakeStaffRepository())
            .CaptureAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, TestTenantId);

        Assert.Equal(CommandStatus.Refused, result.Status);
        Assert.Empty(repository.ReportingSnapshots);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void The_capture_window_is_the_period_end_through_seven_days_after(int daysAfterEnd, bool allowed)
    {
        var end = new DateOnly(2026, 9, 20);

        Assert.Equal(allowed, ReportingSnapshotService.RefusalFor(end, end.AddDays(daysAfterEnd)) is null);
    }

    [Fact]
    public void A_snapshot_recorded_before_the_window_existed_says_how_late_it_was_captured()
    {
        var late = new ReportingSnapshot
        {
            SnapshotKey = Guid.NewGuid(), CapturedAtUtc = new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc),
            PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31),
            TotalEstimatedHours = 0, TotalActualHours = 0, TotalVarianceHours = 0, OpenWorkItems = 0, BlockedWorkItems = 0
        };

        Assert.Equal(61, ReportingSnapshotService.CapturedLateByDays(late));
        Assert.Null(ReportingSnapshotService.CapturedLateByDays(late with { CapturedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc) }));
    }
}
