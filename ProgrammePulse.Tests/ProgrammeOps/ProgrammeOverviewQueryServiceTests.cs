using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class ProgrammeOverviewQueryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ProgrammeOverviewQueryService BuildSut(FakeProgrammeRepository repository, FakeStaffRepository staff) =>
        new(repository, staff, new FixedTimeProvider(Now));

    private static (Programme Programme, Project Project, Workstream Workstream) SeedHierarchy(FakeProgrammeRepository repository)
    {
        var programme = new Programme
        {
            ProgrammeKey = Guid.NewGuid(),
            Name = "Drama",
            TenantId = TestTenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        var project = new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = programme.ProgrammeKey,
            Name = "Series 4",
            TenantId = TestTenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        var workstream = new Workstream
        {
            WorkstreamKey = Guid.NewGuid(),
            ProjectKey = project.ProjectKey,
            Name = "Post-production",
            TenantId = TestTenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

        repository.Programmes.Add(programme);
        repository.Projects.Add(project);
        repository.Workstreams.Add(workstream);

        return (programme, project, workstream);
    }

    private static WorkItem MakeWorkItem(Guid workstreamKey, WorkItemLifecycleStage stage, DateTime? dueDateUtc = null, bool isMilestone = false, Guid? assignedStaffKey = null, decimal? estimatedHours = null) => new()
    {
        WorkItemKey = Guid.NewGuid(),
        WorkstreamKey = workstreamKey,
        Title = "Item",
        Stage = stage,
        IsMilestone = isMilestone,
        AssignedStaffKey = assignedStaffKey,
        DueDateUtc = dueDateUtc,
        EstimatedHours = estimatedHours,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    /// <summary>
    /// BuildResourceCapacity groups by WorkItemAllocation, not
    /// WorkItem.AssignedStaffKey — real data always has both (ClickUpMappingService
    /// populates them together), so tests exercising resource capacity must
    /// seed an allocation per assignee, same as this helper does for the
    /// single-assignee case.
    /// </summary>
    private static WorkItemAllocation MakeAllocation(Guid workItemKey, Guid staffKey, bool isPrimary = true) => new()
    {
        AllocationKey = Guid.NewGuid(),
        WorkItemKey = workItemKey,
        StaffKey = staffKey,
        IsPrimary = isPrimary,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime
    };

    [Fact]
    public async Task BuildOverviewAsync_counts_open_blocked_and_overdue_work_items()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);

        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Done));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Blocked));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(-1)));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(5)));

        var sut = BuildSut(repository, new FakeStaffRepository());
        var overview = await sut.BuildOverviewAsync(TestTenantId);

        Assert.Single(overview.ProgrammeHealth);
        Assert.Equal("3", overview.KpiCards.Single(c => c.Label == "Open Work Items").Value);
        Assert.Equal("1", overview.KpiCards.Single(c => c.Label == "Blocked").Value);
        Assert.Equal("1", overview.KpiCards.Single(c => c.Label == "Overdue").Value);
    }

    [Fact]
    public async Task BuildOverviewAsync_computes_workstream_percent_complete()
    {
        var repository = new FakeProgrammeRepository();
        var (programme, project, workstream) = SeedHierarchy(repository);

        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Done));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Done));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress));

        var sut = BuildSut(repository, new FakeStaffRepository());
        var overview = await sut.BuildOverviewAsync(TestTenantId);

        var row = Assert.Single(overview.WorkstreamStatuses);
        Assert.Equal(programme.Name, row.ProgrammeName);
        Assert.Equal(project.Name, row.ProjectName);
        Assert.Equal(3, row.TotalItems);
        Assert.Equal(2, row.DoneItems);
        Assert.Equal(67, row.PercentComplete);
    }

    [Fact]
    public async Task BuildOverviewAsync_never_includes_cost_or_rate_data()
    {
        var repository = new FakeProgrammeRepository();
        SeedHierarchy(repository);

        var sut = BuildSut(repository, new FakeStaffRepository());
        var overview = await sut.BuildOverviewAsync(TestTenantId);

        var overviewType = overview.GetType();
        var resourceCapacityType = typeof(ProgrammePulse.Models.ViewModels.ProgrammeOverview.ResourceCapacityViewModel);

        // Matches "CostPerHour" / "RateCurrency" (StaffRate's actual fields)
        // without false-positiving on unrelated names like "GeneratedAtUtc"
        // (which contains "rate" as a substring of "Generated").
        static bool NameLooksLikeCostOrRateField(string name) =>
            name.Contains("Cost", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Rate", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Rate", StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(overviewType.GetProperties(), p => NameLooksLikeCostOrRateField(p.Name));
        Assert.DoesNotContain(resourceCapacityType.GetProperties(), p => NameLooksLikeCostOrRateField(p.Name));
    }

    [Fact]
    public async Task BuildOverviewAsync_aggregates_resource_capacity_for_active_staff_only()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);

        var activeStaff = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = 1,
            FullName = "Active Person",
            Email = "active@example.com",
            IsActive = true,
            DefaultWorkHoursPerWeek = 37.5m,
            TenantId = TestTenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        var inactiveStaff = activeStaff with { StaffKey = Guid.NewGuid(), MemberId = 2, FullName = "Left Person", IsActive = false };

        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(activeStaff);
        staffRepository.Staff.Add(inactiveStaff);

        var item1 = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: activeStaff.StaffKey, estimatedHours: 4m);
        var item2 = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: activeStaff.StaffKey, estimatedHours: 3m);
        var item3 = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Done, assignedStaffKey: activeStaff.StaffKey, estimatedHours: 100m);
        var item4 = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: inactiveStaff.StaffKey, estimatedHours: 10m);
        repository.WorkItems.Add(item1);
        repository.WorkItems.Add(item2);
        repository.WorkItems.Add(item3);
        repository.WorkItems.Add(item4);
        repository.Allocations.Add(MakeAllocation(item1.WorkItemKey, activeStaff.StaffKey));
        repository.Allocations.Add(MakeAllocation(item2.WorkItemKey, activeStaff.StaffKey));
        repository.Allocations.Add(MakeAllocation(item3.WorkItemKey, activeStaff.StaffKey));
        repository.Allocations.Add(MakeAllocation(item4.WorkItemKey, inactiveStaff.StaffKey));

        var sut = BuildSut(repository, staffRepository);
        var overview = await sut.BuildOverviewAsync(TestTenantId);

        var row = Assert.Single(overview.ResourceCapacity);
        Assert.Equal("Active Person", row.StaffFullName);
        Assert.Equal(2, row.AssignedOpenItems);
        Assert.Equal(7m, row.EstimatedHoursOutstanding);
    }

    private static PlannedAllocation MakePlanned(Guid projectKey, Guid? staffKey, DateTime? startUtc, DateTime? endUtc, decimal? hours = null, string externalId = "b") => new()
    {
        PlannedAllocationKey = Guid.NewGuid(),
        ProjectKey = projectKey,
        StaffKey = staffKey,
        Title = "Booking",
        StartUtc = startUtc,
        EndUtc = endUtc,
        AllocatedHours = hours,
        ExternalSource = "HubPlanner",
        ExternalId = externalId + Guid.NewGuid().ToString("N"),
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    private static StaffProfile ActivePerson(string name) => new()
    {
        StaffKey = Guid.NewGuid(),
        MemberId = 1,
        FullName = name,
        Email = $"{name.Replace(' ', '.')}@example.com",
        IsActive = true,
        DefaultWorkHoursPerWeek = 37.5m,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    [Fact]
    public async Task Elapsed_planned_allocations_never_count_as_done_work_or_percent_complete()
    {
        var repository = new FakeProgrammeRepository();
        var (_, project, workstream) = SeedHierarchy(repository);
        var person = ActivePerson("Booked Person");
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(person);

        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress));
        // Three bookings whose end dates are long past — under the old
        // mapping these were Done work items and made this workstream 75%
        // complete.
        for (var i = 0; i < 3; i++)
        {
            repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, person.StaffKey, Now.UtcDateTime.AddDays(-30), Now.UtcDateTime.AddDays(-28), hours: 8m));
        }

        var overview = await BuildSut(repository, staffRepository).BuildOverviewAsync(TestTenantId);

        var row = Assert.Single(overview.WorkstreamStatuses);
        Assert.Equal(1, row.TotalItems);
        Assert.Equal(0, row.DoneItems);
        Assert.Equal(0, row.PercentComplete);
        Assert.Equal("1", overview.KpiCards.Single(c => c.Label == "Open Work Items").Value);
        Assert.Empty(overview.PlannedAllocations);
        Assert.Equal(0, overview.PlannedCoverage.TotalInWindow);
    }

    [Fact]
    public async Task Planned_allocations_in_the_window_are_summed_per_active_person_with_coverage_gaps_reported()
    {
        var repository = new FakeProgrammeRepository();
        var (_, project, _) = SeedHierarchy(repository);
        var alex = ActivePerson("Alex Editor");
        var sam = ActivePerson("Sam Grader");
        var leaver = ActivePerson("Left Person") with { IsActive = false };
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(alex);
        staffRepository.Staff.Add(sam);
        staffRepository.Staff.Add(leaver);

        var today = Now.UtcDateTime;
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, alex.StaffKey, today.AddDays(1), today.AddDays(3), hours: 16m));
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, alex.StaffKey, today.AddDays(-2), today.AddDays(1), hours: 4m));       // straddles the window start
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, alex.StaffKey, today.AddDays(5), today.AddDays(6)));                  // no hours recorded
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, sam.StaffKey, today.AddDays(10), today.AddDays(12)));                 // sam: nothing with hours
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, null, today.AddDays(2), today.AddDays(4), hours: 8m));                // unresolved person
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, leaver.StaffKey, today.AddDays(2), today.AddDays(4), hours: 8m));     // inactive person
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, alex.StaffKey, today.AddDays(40), today.AddDays(45), hours: 99m));    // beyond the window
        repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, alex.StaffKey, null, null, hours: 50m));                              // undated

        var overview = await BuildSut(repository, staffRepository).BuildOverviewAsync(TestTenantId);

        Assert.Equal(2, overview.PlannedAllocations.Count);
        var alexRow = overview.PlannedAllocations.Single(r => r.StaffFullName == "Alex Editor");
        Assert.Equal(3, alexRow.AllocationCount);
        Assert.Equal(20m, alexRow.BookedHours);
        Assert.Equal(1, alexRow.AllocationsWithoutHours);
        // 16h wholly inside plus a third of the 4h booking that started two
        // days ago, over a four-week window.
        Assert.Equal(4.3m, alexRow.BookedHoursPerWeek);
        Assert.Equal(alex.DefaultWorkHoursPerWeek, alexRow.CapacityHoursPerWeek);
        Assert.False(alexRow.IsBookedOverCapacity);
        var samRow = overview.PlannedAllocations.Single(r => r.StaffFullName == "Sam Grader");
        Assert.Null(samRow.BookedHours);
        Assert.Null(samRow.BookedHoursPerWeek);
        Assert.Equal(1, samRow.AllocationsWithoutHours);

        var coverage = overview.PlannedCoverage;
        Assert.Equal(6, coverage.TotalInWindow);
        Assert.Equal(2, coverage.WithoutResolvedStaff);
        Assert.Equal(2, coverage.WithoutHours);
        Assert.Equal(1, coverage.Undated);
        Assert.Equal(today, coverage.WindowStartUtc);
        Assert.Equal(today.AddDays(ProgrammeOverviewQueryService.PlannedWindowDays), coverage.WindowEndUtc);
    }

    [Fact]
    public async Task Someone_booked_past_their_contracted_week_is_flagged()
    {
        var repository = new FakeProgrammeRepository();
        var (_, project, _) = SeedHierarchy(repository);
        var owen = ActivePerson("Owen Specialist");
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(owen);

        // 50 hours a week for each of the four weeks in the window.
        var today = Now.UtcDateTime;
        for (var week = 0; week < 4; week++)
            repository.PlannedAllocations.Add(MakePlanned(project.ProjectKey, owen.StaffKey, today.AddDays(7 * week), today.AddDays(7 * week + 7), hours: 50m));

        var overview = await BuildSut(repository, staffRepository).BuildOverviewAsync(TestTenantId);

        var row = Assert.Single(overview.PlannedAllocations);
        Assert.Equal(50m, row.BookedHoursPerWeek);
        Assert.True(row.IsBookedOverCapacity);
    }

    [Fact]
    public async Task Open_items_without_an_estimate_are_counted_not_treated_as_zero_hours()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var person = ActivePerson("Estimating Person");
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(person);

        var estimated = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: person.StaffKey, estimatedHours: 5m);
        var unestimated = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Ready, assignedStaffKey: person.StaffKey);
        repository.WorkItems.Add(estimated);
        repository.WorkItems.Add(unestimated);
        repository.Allocations.Add(MakeAllocation(estimated.WorkItemKey, person.StaffKey));
        repository.Allocations.Add(MakeAllocation(unestimated.WorkItemKey, person.StaffKey));

        var overview = await BuildSut(repository, staffRepository).BuildOverviewAsync(TestTenantId);

        var row = Assert.Single(overview.ResourceCapacity);
        Assert.Equal(2, row.AssignedOpenItems);
        Assert.Equal(5m, row.EstimatedHoursOutstanding);
        Assert.Equal(1, row.OpenItemsWithoutEstimate);
    }

    [Fact]
    public async Task BuildOverviewAsync_credits_estimated_hours_only_to_the_primary_allocation()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);

        var first = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            MemberId = 1,
            FullName = "First Person",
            Email = "first@example.com",
            IsActive = true,
            DefaultWorkHoursPerWeek = 37.5m,
            TenantId = TestTenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        var second = first with { StaffKey = Guid.NewGuid(), MemberId = 2, FullName = "Second Person", Email = "second@example.com" };

        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(first);
        staffRepository.Staff.Add(second);

        var sharedItem = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: first.StaffKey, estimatedHours: 6m);
        repository.WorkItems.Add(sharedItem);
        repository.Allocations.Add(MakeAllocation(sharedItem.WorkItemKey, first.StaffKey, isPrimary: true));
        repository.Allocations.Add(MakeAllocation(sharedItem.WorkItemKey, second.StaffKey, isPrimary: false));

        var sut = BuildSut(repository, staffRepository);
        var overview = await sut.BuildOverviewAsync(TestTenantId);

        // Only First Person (the primary allocation) is credited the item's
        // hours — Second Person is a co-assignee with no other allocation of
        // their own, so they don't appear in resource capacity at all rather
        // than inflating the total workload for an item only one person is
        // actually accountable for.
        var row = Assert.Single(overview.ResourceCapacity);
        Assert.Equal("First Person", row.StaffFullName);
        Assert.Equal(6m, row.EstimatedHoursOutstanding);
    }
    // Design and PMO data review, 30 Sep 2026 (B3): the overview said 14
    // overdue and the Evidence Check said 9 for the same data. One rule now.
    [Fact]
    public async Task The_overview_and_the_evidence_check_count_the_same_items_as_overdue()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(-3)));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Blocked, dueDateUtc: Now.UtcDateTime.AddDays(-10), isMilestone: true));
        // Status can't be read: past due, but not counted as overdue on either page.
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Unmapped, dueDateUtc: Now.UtcDateTime.AddDays(-2), isMilestone: true));
        // Due earlier today: not late until tomorrow on either page.
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Ready, dueDateUtc: Now.UtcDateTime.Date.AddHours(1)));

        var overview = await BuildSut(repository, new FakeStaffRepository()).BuildOverviewAsync(TestTenantId);
        var check = EvidenceCheckCalculator.Evaluate(
            new EvidenceCheckInput(repository.Projects, repository.Workstreams, repository.WorkItems, [], 0, []), Now.UtcDateTime);

        var checkOverdue = check.Findings.Single(f => f.Key == "overdue");
        Assert.Equal(2m, checkOverdue.Numerator);
        Assert.Equal("2", overview.KpiCards.Single(c => c.Label == "Overdue").Value);
        Assert.Equal(2, overview.WorkstreamStatuses.Sum(w => w.OverdueItems));
        Assert.Equal(2, Assert.Single(overview.ProgrammeHealth).OverdueItems);
        Assert.Equal(1, overview.PastDueWithUnreadableStatus);
        var unread = Assert.Single(overview.Milestones, m => m.IsPastDueWithUnreadableStatus);
        Assert.False(unread.IsOverdue);
    }

    [Fact]
    public async Task Each_headline_figure_carries_its_denominator()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Done));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Blocked));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(-1), isMilestone: true));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Ready, isMilestone: true));

        var overview = await BuildSut(repository, new FakeStaffRepository()).BuildOverviewAsync(TestTenantId);

        var open = overview.KpiCards.Single(c => c.Label == "Open Work Items");
        Assert.Equal((3, 4, "Kpi.OfWorkItems"), (open.Numerator, open.Denominator, open.DenominatorKey));
        var blocked = overview.KpiCards.Single(c => c.Label == "Blocked");
        Assert.Equal((1, 3, "Kpi.OfOpenItems"), (blocked.Numerator, blocked.Denominator, blocked.DenominatorKey));
        var milestones = overview.KpiCards.Single(c => c.Label == "Overdue milestones");
        Assert.Equal((1, 2), (milestones.Numerator, milestones.Denominator));
        Assert.Equal("50%", milestones.SharePercent);
        Assert.DoesNotContain(overview.KpiCards, c => c.Label == "Programmes");
    }

    [Fact]
    public async Task Overdue_work_is_banded_by_days_late_and_split_by_whether_anyone_is_named()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var owner = Guid.NewGuid();
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(-3), assignedStaffKey: owner));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, dueDateUtc: Now.UtcDateTime.AddDays(-7)));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Blocked, dueDateUtc: Now.UtcDateTime.AddDays(-8), assignedStaffKey: owner));
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.Ready, dueDateUtc: Now.UtcDateTime.AddDays(-45)));

        var overview = await BuildSut(repository, new FakeStaffRepository()).BuildOverviewAsync(TestTenantId);

        Assert.Equal(
            [("1 to 7 days", 1, 1), ("8 to 30 days", 1, 0), ("31 days or more", 0, 1)],
            overview.OverdueByAge.Select(b => (b.Label, b.WithOwner, b.WithoutOwner)));
    }

    // A list of people sorted by how much they carry is read from the bottom
    // as a ranking (docs/delivery-load.md), so both people lists go by name.
    [Fact]
    public async Task Workload_rows_are_in_name_order_not_load_order()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var staffRepository = new FakeStaffRepository();
        foreach (var (name, hours) in new[] { ("Zara Light", 2m), ("Adam Heavy", 40m), ("Mo Middle", 10m) })
        {
            var person = new StaffProfile
            {
                StaffKey = Guid.NewGuid(), MemberId = staffRepository.Staff.Count + 1, FullName = name, Email = $"{name.Replace(' ', '.')}@example.com",
                IsActive = true, DefaultWorkHoursPerWeek = 37.5m, TenantId = TestTenantId, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
            };
            staffRepository.Staff.Add(person);
            var item = MakeWorkItem(workstream.WorkstreamKey, WorkItemLifecycleStage.InProgress, assignedStaffKey: person.StaffKey, estimatedHours: hours);
            repository.WorkItems.Add(item);
            repository.Allocations.Add(MakeAllocation(item.WorkItemKey, person.StaffKey));
        }

        var overview = await BuildSut(repository, staffRepository).BuildOverviewAsync(TestTenantId);

        Assert.Equal(["Adam Heavy", "Mo Middle", "Zara Light"], overview.ResourceCapacity.Select(r => r.StaffFullName));
    }
}
