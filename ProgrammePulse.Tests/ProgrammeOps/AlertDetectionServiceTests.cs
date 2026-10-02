using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class AlertDetectionServiceTests
{
    [Theory]
    [InlineData("Drama", 2)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData(null, 3)]
    public async Task Alert_pages_and_counts_share_the_same_team_scope(string? team, int count)
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        var own = MakeStaff() with { Team = "Drama" };
        var other = MakeStaff() with { Team = "Other" };
        staff.Staff.AddRange([own, other]);
        foreach (var person in new[] { own, other })
            await repository.RaiseAlertAsync(new Alert { AlertKey = Guid.NewGuid(), Type = AlertType.NegativeResidualCapacity,
                EntityKey = person.StaffKey, Message = person.FullName, RaisedAtUtc = Now }, TestTenantId);
        await repository.RaiseAlertAsync(new Alert { AlertKey = Guid.NewGuid(), Type = AlertType.WorkstreamBlocked,
            EntityKey = Guid.NewGuid(), Message = "Blocked workstream", RaisedAtUtc = Now }, TestTenantId);
        var service = BuildSut(repository, staff);
        var page = await service.GetOpenAlertsPageAsync(TestTenantId, PageRequest.First(), teamFilter: team);
        Assert.Equal(count, page.Items.Count);
        Assert.Equal(count, await service.CountOpenAlertsAsync(TestTenantId, teamFilter: team));
        if (team is not null) Assert.DoesNotContain(page.Items, a => a.EntityKey == other.StaffKey);
    }

    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0); // a Wednesday
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AlertDetectionService BuildSut(FakeProgrammeRepository repository, FakeStaffRepository? staffRepository = null, FakeAvailabilityRepository? availability = null) =>
        new(repository, staffRepository ?? new FakeStaffRepository(), availability ?? new FakeAvailabilityRepository(), reportingQueryService: null!);

    private static (Workstream Workstream, WorkItem Item) SeedBlockedWorkstream(FakeProgrammeRepository repository)
    {
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Drama", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var project = new Project { ProjectKey = Guid.NewGuid(), TenantId = TestTenantId, ProgrammeKey = programme.ProgrammeKey, Name = "Series 4", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workstream = new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = TestTenantId, ProjectKey = project.ProjectKey, Name = "Post", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var item = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "Edit", Stage = WorkItemLifecycleStage.Blocked, CreatedAtUtc = Now, UpdatedAtUtc = Now };

        repository.Programmes.Add(programme);
        repository.Projects.Add(project);
        repository.Workstreams.Add(workstream);
        repository.WorkItems.Add(item);

        return (workstream, item);
    }

    private static ContributorCapacityRowViewModel MakeContributor(decimal residual) =>
        new(Guid.NewGuid(), "Jamie Gardner", "Drama", 35m, 0m, 40m, residual, 114, 114);

    private static StaffProfile MakeStaff(decimal hoursPerWeek = 37.5m) => new()
    {
        StaffKey = Guid.NewGuid(),
        MemberId = 1,
        FullName = "Alex Producer",
        Email = "alex@example.com",
        IsActive = true,
        DefaultWorkHoursPerWeek = hoursPerWeek,
        TenantId = TestTenantId,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static WorkItem MakeWorkItem(WorkItemLifecycleStage stage, DateTime? dueDateUtc, decimal? estimatedHours) => new()
    {
        WorkItemKey = Guid.NewGuid(),
        TenantId = TestTenantId,
        WorkstreamKey = Guid.NewGuid(),
        Title = "Item",
        Stage = stage,
        DueDateUtc = dueDateUtc,
        EstimatedHours = estimatedHours,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    [Fact]
    public async Task DetectAndRaiseAsync_raises_an_alert_for_a_workstream_with_a_blocked_item()
    {
        var repository = new FakeProgrammeRepository();
        var (workstream, _) = SeedBlockedWorkstream(repository);
        var sut = BuildSut(repository);

        await sut.DetectAndRaiseAsync([], Now, TestTenantId);

        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(AlertType.WorkstreamBlocked, alert.Type);
        Assert.Equal(workstream.WorkstreamKey, alert.EntityKey);
        Assert.Null(alert.AcknowledgedAtUtc);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_does_not_duplicate_an_alert_that_is_already_open()
    {
        var repository = new FakeProgrammeRepository();
        SeedBlockedWorkstream(repository);
        var sut = BuildSut(repository);

        await sut.DetectAndRaiseAsync([], Now, TestTenantId);
        await sut.DetectAndRaiseAsync([], Now.AddMinutes(5), TestTenantId); // simulates a second page load

        Assert.Single(repository.Alerts);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_raises_a_new_alert_once_the_old_one_is_acknowledged()
    {
        var repository = new FakeProgrammeRepository();
        SeedBlockedWorkstream(repository);
        var sut = BuildSut(repository);

        await sut.DetectAndRaiseAsync([], Now, TestTenantId);
        var first = Assert.Single(repository.Alerts);
        await sut.AcknowledgeAsync(first.AlertKey, null, Now.AddMinutes(1), TestTenantId);

        await sut.DetectAndRaiseAsync([], Now.AddMinutes(2), TestTenantId);

        Assert.Equal(2, repository.Alerts.Count);
        Assert.Single((await sut.GetOpenAlertsPageAsync(TestTenantId, PageRequest.First())).Items);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_raises_an_alert_for_negative_residual_capacity()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var overAllocated = MakeContributor(residual: -5m);
        var healthy = MakeContributor(residual: 3m);

        await sut.DetectAndRaiseAsync([overAllocated, healthy], Now, TestTenantId);

        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(AlertType.NegativeResidualCapacity, alert.Type);
        Assert.Equal(overAllocated.StaffKey, alert.EntityKey);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_raises_low_utilisation_when_a_contributor_with_available_hours_logs_well_under_the_threshold()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        // 40 baseline, no leave => 40 available, 10 logged => 25% utilisation.
        var underUtilised = new ContributorCapacityRowViewModel(Guid.NewGuid(), "Sam Editor", "Drama", 40m, 0m, 10m, 30m, 25, 25);

        await sut.DetectAndRaiseAsync([underUtilised], Now, TestTenantId);

        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(AlertType.LowUtilisation, alert.Type);
        Assert.Equal(underUtilised.StaffKey, alert.EntityKey);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_does_not_raise_low_utilisation_for_a_contributor_with_zero_available_hours()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        // Fully on leave: 40 baseline, 40 leave => 0 available, 0 logged =>
        // 0% utilisation, but there was nothing to log against, not bench time.
        var onLeave = new ContributorCapacityRowViewModel(Guid.NewGuid(), "Sam Editor", "Drama", 40m, 40m, 0m, 0m, 0, 0);

        await sut.DetectAndRaiseAsync([onLeave], Now, TestTenantId);

        Assert.Empty(repository.Alerts);
    }

    [Fact]
    public async Task AcknowledgeAsync_removes_the_alert_from_the_open_list()
    {
        var repository = new FakeProgrammeRepository();
        SeedBlockedWorkstream(repository);
        var sut = BuildSut(repository);
        await sut.DetectAndRaiseAsync([], Now, TestTenantId);
        var alert = Assert.Single((await sut.GetOpenAlertsPageAsync(TestTenantId, PageRequest.First())).Items);

        var staffKey = Guid.NewGuid();
        await sut.AcknowledgeAsync(alert.AlertKey, staffKey, Now.AddHours(1), TestTenantId);

        Assert.Empty((await sut.GetOpenAlertsPageAsync(TestTenantId, PageRequest.First())).Items);
        var stored = Assert.Single(repository.Alerts);
        Assert.Equal(staffKey, stored.AcknowledgedByStaffKey);
        Assert.NotNull(stored.AcknowledgedAtUtc);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_raises_an_upcoming_over_allocation_alert_when_committed_work_exceeds_window_capacity()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var person = MakeStaff(hoursPerWeek: 35m); // 7 hrs/day
        staffRepository.Staff.Add(person);

        // 14-day forward window from Now (Wed 2026-09-30) is 10 weekdays =
        // 70 hours baseline, no leave => 70 available. Two items due inside
        // the window carry 40 + 40 = 80 estimated hours between them, both
        // primary-allocated to this person => over-committed.
        var item1 = MakeWorkItem(WorkItemLifecycleStage.InProgress, Now.AddDays(3), 40m);
        var item2 = MakeWorkItem(WorkItemLifecycleStage.Backlog, Now.AddDays(10), 40m);
        repository.WorkItems.Add(item1);
        repository.WorkItems.Add(item2);
        repository.Allocations.Add(new WorkItemAllocation { AllocationKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = item1.WorkItemKey, StaffKey = person.StaffKey, IsPrimary = true, CreatedAtUtc = Now });
        repository.Allocations.Add(new WorkItemAllocation { AllocationKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = item2.WorkItemKey, StaffKey = person.StaffKey, IsPrimary = true, CreatedAtUtc = Now });

        var sut = BuildSut(repository, staffRepository);
        await sut.DetectAndRaiseAsync([], Now, TestTenantId);

        var alert = Assert.Single(repository.Alerts);
        Assert.Equal(AlertType.UpcomingOverAllocation, alert.Type);
        Assert.Equal(person.StaffKey, alert.EntityKey);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_does_not_raise_upcoming_over_allocation_for_a_non_primary_allocation()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var person = MakeStaff();
        staffRepository.Staff.Add(person);

        var item = MakeWorkItem(WorkItemLifecycleStage.InProgress, Now.AddDays(3), 1000m); // absurdly over capacity if it counted
        repository.WorkItems.Add(item);
        repository.Allocations.Add(new WorkItemAllocation { AllocationKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = item.WorkItemKey, StaffKey = person.StaffKey, IsPrimary = false, CreatedAtUtc = Now });

        var sut = BuildSut(repository, staffRepository);
        await sut.DetectAndRaiseAsync([], Now, TestTenantId);

        Assert.Empty(repository.Alerts);
    }

    [Fact]
    public async Task DetectAndRaiseAsync_does_not_raise_upcoming_over_allocation_for_work_due_outside_the_window()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var person = MakeStaff(hoursPerWeek: 5m); // tiny capacity, easy to exceed if counted
        staffRepository.Staff.Add(person);

        var item = MakeWorkItem(WorkItemLifecycleStage.InProgress, Now.AddDays(30), 100m); // due well beyond the 14-day window
        repository.WorkItems.Add(item);
        repository.Allocations.Add(new WorkItemAllocation { AllocationKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = item.WorkItemKey, StaffKey = person.StaffKey, IsPrimary = true, CreatedAtUtc = Now });

        var sut = BuildSut(repository, staffRepository);
        await sut.DetectAndRaiseAsync([], Now, TestTenantId);

        Assert.Empty(repository.Alerts);
    }

    private sealed class FakeAvailabilityRepository : IAvailabilityRepository
    {
        public readonly List<Availability> Rows = [];

        public Task<IReadOnlyList<Availability>> GetForStaffAsync(Guid staffKey, DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<Availability>>(
                Rows.Where(a => a.StaffKey == staffKey && a.Date >= from && a.Date <= to).ToList());

        public Task<Availability> CreateAsync(Availability availability)
        {
            Rows.Add(availability);
            return Task.FromResult(availability);
        }
    }
}
