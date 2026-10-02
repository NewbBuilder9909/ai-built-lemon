using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ProgrammeOps;

public sealed class EstimateCalibrationTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EstimatorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EstimatorB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeBaselines : IEstimateBaselineRepository
    {
        public List<EstimateBaseline> Rows { get; } = [];
        public Task<IReadOnlyList<EstimateBaseline>> GetForTenantAsync(Guid tenantId) =>
            Task.FromResult<IReadOnlyList<EstimateBaseline>>(Rows.Where(r => r.TenantId == tenantId).ToArray());
        public Task<EstimateBaseline> CaptureAsync(EstimateBaseline baseline)
        {
            Rows.Add(baseline);
            return Task.FromResult(baseline);
        }
        public Task<EstimateBaseline?> ReviewAsync(Guid tenantId, Guid key, bool comparable, string? note, Guid reviewer, DateTime at)
        {
            var row = Rows.FirstOrDefault(r => r.TenantId == tenantId && r.EstimateBaselineKey == key);
            if (row is null) return Task.FromResult<EstimateBaseline?>(null);
            var reviewed = row with { IsComparable = comparable, ReviewNote = note, ReviewedByStaffKey = reviewer, ReviewedAtUtc = at };
            Rows[Rows.IndexOf(row)] = reviewed;
            return Task.FromResult<EstimateBaseline?>(reviewed);
        }
    }

    private static WorkItem Item(Guid key, Guid tenant, WorkItemLifecycleStage stage, decimal? estimate = 10m) => new()
    {
        WorkItemKey = key, TenantId = tenant, WorkstreamKey = Guid.NewGuid(), Title = "Task",
        Stage = stage, EstimatedHours = estimate, CreatedAtUtc = Now, UpdatedAtUtc = Now
    };

    private static EstimateBaseline Baseline(Guid key, Guid tenant, Guid estimator, bool? reviewed = true) => new()
    {
        EstimateBaselineKey = Guid.NewGuid(), TenantId = tenant, WorkItemKey = key,
        EstimatorStaffKey = estimator, OriginalEffortHours = 10m, CapturedAtUtc = Now.AddDays(-10),
        ReviewedAtUtc = reviewed is null ? null : Now, IsComparable = reviewed ?? false
    };

    private static TimeEntry Time(Guid key, Guid tenant, decimal hours) => new()
    {
        TimeEntryKey = Guid.NewGuid(), TenantId = tenant, WorkItemKey = key,
        DurationHours = hours, StartedAtUtc = Now, CreatedAtUtc = Now, UpdatedAtUtc = Now
    };

    [Fact]
    public void Only_reviewed_completed_comparable_work_with_actuals_informs_the_factor()
    {
        var baselines = new List<EstimateBaseline>();
        var items = new List<WorkItem>();
        var actuals = new List<TimeEntry>();
        for (var n = 0; n < 10; n++)
        {
            var key = Guid.NewGuid();
            baselines.Add(Baseline(key, Tenant, n < 5 ? EstimatorA : EstimatorB));
            items.Add(Item(key, Tenant, WorkItemLifecycleStage.Done));
            actuals.Add(Time(key, Tenant, n < 5 ? 30m : 10m));
        }
        var pendingKey = Guid.NewGuid();
        baselines.Add(Baseline(pendingKey, Tenant, EstimatorA, reviewed: null));
        items.Add(Item(pendingKey, Tenant, WorkItemLifecycleStage.Done));
        actuals.Add(Time(pendingKey, Tenant, 100m));
        var excludedKey = Guid.NewGuid();
        baselines.Add(Baseline(excludedKey, Tenant, EstimatorA, reviewed: false));
        items.Add(Item(excludedKey, Tenant, WorkItemLifecycleStage.Done));
        actuals.Add(Time(excludedKey, Tenant, 100m));
        var foreignKey = Guid.NewGuid();
        baselines.Add(Baseline(foreignKey, OtherTenant, EstimatorA));
        items.Add(Item(foreignKey, Tenant, WorkItemLifecycleStage.Done));
        actuals.Add(Time(foreignKey, Tenant, 100m));

        var result = EstimateCalibrationCalculator.Calculate(Tenant, baselines, items, actuals);

        Assert.Equal(10, result.Samples.Count);
        Assert.Equal(1, result.PendingReviewCount);
        Assert.Equal(1, result.ExcludedCount);
        Assert.Equal(2m, result.PortfolioMedianRatio);
        Assert.Equal(3m, result.PortfolioP80Ratio);
        Assert.Equal(2.33m, result.Estimators.Single(e => e.EstimatorStaffKey == EstimatorA).AdvisoryPlanningFactor);
    }

    [Fact]
    public void Insufficient_history_does_not_produce_a_portfolio_or_person_adjustment()
    {
        var key = Guid.NewGuid();
        var result = EstimateCalibrationCalculator.Calculate(Tenant,
            [Baseline(key, Tenant, EstimatorA)], [Item(key, Tenant, WorkItemLifecycleStage.Done)], [Time(key, Tenant, 35m)]);
        Assert.Null(result.PortfolioMedianRatio);
        Assert.Null(Assert.Single(result.Estimators).AdvisoryPlanningFactor);
    }

    [Fact]
    public void Time_from_another_tenant_cannot_change_the_same_work_item_result()
    {
        var key = Guid.NewGuid();
        var result = EstimateCalibrationCalculator.Calculate(Tenant,
            [Baseline(key, Tenant, EstimatorA)],
            [Item(key, Tenant, WorkItemLifecycleStage.Done)],
            [Time(key, Tenant, 20m), Time(key, OtherTenant, 100m)]);

        Assert.Equal(2m, Assert.Single(result.Samples).ActualToEstimateRatio);
    }

    [Fact]
    public async Task Board_projection_contains_aggregate_evidence_but_no_people_or_capture_options()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        staff.Staff.Add(new StaffProfile { StaffKey = EstimatorA, TenantId = Tenant, MemberId = 1,
            FullName = "Jim", Email = "jim@example.com", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        var key = Guid.NewGuid();
        repository.WorkItems.Add(Item(key, Tenant, WorkItemLifecycleStage.Done));
        repository.TimeEntries.Add(Time(key, Tenant, 30m));
        var baselines = new FakeBaselines();
        baselines.Rows.Add(Baseline(key, Tenant, EstimatorA));
        var service = new EstimateCalibrationService(baselines, repository, staff, new FakeAuditLogRepository(), new FixedTimeProvider());

        var board = await service.BuildReportAsync(Tenant, includePeople: false);
        var admin = await service.BuildReportAsync(Tenant, includePeople: true);

        Assert.Equal(1, board.SampleCount);
        Assert.Empty(board.Estimators);
        Assert.Empty(board.Baselines);
        Assert.Empty(board.EstimatorOptions);
        Assert.Single(admin.Estimators);
        Assert.Equal("Jim", admin.Estimators[0].Name);
    }

    [Fact]
    public async Task Capture_requires_an_unstarted_item_with_estimate_and_active_estimator_in_tenant()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        staff.Staff.Add(new StaffProfile { StaffKey = EstimatorA, TenantId = Tenant, MemberId = 1,
            FullName = "Jim", Email = "jim@example.com", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        var ready = Guid.NewGuid();
        repository.WorkItems.Add(Item(ready, Tenant, WorkItemLifecycleStage.Ready));
        var started = Guid.NewGuid();
        repository.WorkItems.Add(Item(started, Tenant, WorkItemLifecycleStage.InProgress));
        var baselines = new FakeBaselines();
        var audit = new FakeAuditLogRepository();
        var service = new EstimateCalibrationService(baselines, repository, staff, audit, new FixedTimeProvider());

        var captured = await service.CaptureAsync(Tenant, ready, EstimatorA, actorMemberId: 42);

        Assert.Equal(10m, captured.OriginalEffortHours);
        Assert.Null(captured.ReviewedAtUtc);
        // Auditing moved from the controller into the service, so every caller audits.
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(("CaptureRequested", 42, Tenant), (entry.Action, entry.ActorMemberId, entry.TenantId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(Tenant, started, EstimatorA, actorMemberId: 42));
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => service.CaptureAsync(OtherTenant, ready, EstimatorA, actorMemberId: 42));
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Review_rejects_a_comparable_sample_without_actual_time()
    {
        var repository = new FakeProgrammeRepository();
        var key = Guid.NewGuid();
        repository.WorkItems.Add(Item(key, Tenant, WorkItemLifecycleStage.Done));
        var baselines = new FakeBaselines();
        var baseline = Baseline(key, Tenant, EstimatorA, reviewed: null);
        baselines.Rows.Add(baseline);
        var service = new EstimateCalibrationService(baselines, repository, new FakeStaffRepository(), new FakeAuditLogRepository(), new FixedTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReviewAsync(Tenant, baseline.EstimateBaselineKey, EstimatorA, comparable: true, note: null, actorMemberId: 42));
    }
}
