using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;
using ProgrammePulse.Services.Integrations.HubPlanner;

namespace ProgrammePulse.Tests.Integrations;

public class HubPlannerMappingServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static HubPlannerMappingService BuildSut(FakeProgrammeRepository repository) =>
        new(repository, new FixedTimeProvider(Now));

    [Fact]
    public async Task MapRootProgrammeAsync_upserts_a_single_synthetic_programme()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);

        var first = await sut.MapRootProgrammeAsync(TestTenantId);
        var second = await sut.MapRootProgrammeAsync(TestTenantId);

        Assert.Equal("Hub Planner", first.Name);
        Assert.Equal("HubPlanner", first.ExternalSource);
        Assert.Equal(first.ProgrammeKey, second.ProgrammeKey);
        Assert.Single(repository.Programmes);
    }

    [Fact]
    public async Task MapProjectAsync_maps_name_and_external_id()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var programmeKey = Guid.NewGuid();

        var project = await sut.MapProjectAsync(new HubPlannerProjectDto { Id = "proj-1", Name = "Drama Season 4" }, programmeKey, TestTenantId);

        Assert.Equal("Drama Season 4", project.Name);
        Assert.Equal("HubPlanner", project.ExternalSource);
        Assert.Equal("proj-1", project.ExternalId);
        Assert.Equal(programmeKey, project.ProgrammeKey);
    }

    [Fact]
    public async Task MapBookingAsync_creates_a_planned_allocation_and_never_a_work_item()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var projectKey = Guid.NewGuid();

        // A booking whose end date is long past — under the old mapping this
        // became a Done work item and counted as delivery progress.
        var allocation = await sut.MapBookingAsync(
            new HubPlannerBookingDto { Id = "b1", Resource = "res-1", Start = "2026-01-01T09:00", End = "2026-01-05T17:00", Type = "SCHEDULED" },
            projectKey, null, TestTenantId);

        Assert.Equal(projectKey, allocation.ProjectKey);
        Assert.Equal("HubPlanner", allocation.ExternalSource);
        Assert.Equal("b1", allocation.ExternalId);
        Assert.Equal("res-1", allocation.ExternalResourceId);
        Assert.Equal("SCHEDULED", allocation.RawType);
        Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), allocation.StartUtc);
        Assert.Equal(new DateTime(2026, 1, 5, 17, 0, 0, DateTimeKind.Utc), allocation.EndUtc);
        Assert.Single(repository.PlannedAllocations);
        Assert.Empty(repository.WorkItems);
        Assert.Empty(repository.Workstreams);
    }

    [Fact]
    public async Task MapBookingAsync_falls_back_from_title_to_category_to_default()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var projectKey = Guid.NewGuid();

        var withTitle = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b1", Title = "Edit rough cut", CategoryName = "General" }, projectKey, null, TestTenantId);
        var withCategoryOnly = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b2", Title = "", CategoryName = "General" }, projectKey, null, TestTenantId);
        var withNeither = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b3" }, projectKey, null, TestTenantId);

        Assert.Equal("Edit rough cut", withTitle.Title);
        Assert.Equal("General", withCategoryOnly.Title);
        Assert.Equal("Booking", withNeither.Title);
    }

    [Fact]
    public async Task MapBookingAsync_copies_hours_only_for_state_hours_and_percent_only_for_state_percentage()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var projectKey = Guid.NewGuid();

        var hours = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b1", State = "STATE_HOURS", StateValue = 6 }, projectKey, null, TestTenantId);
        var percentage = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b2", State = "STATE_PERCENTAGE", StateValue = 50 }, projectKey, null, TestTenantId);
        var unknown = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b3", State = "STATE_SOMETHING_ELSE", StateValue = 7 }, projectKey, null, TestTenantId);

        Assert.Equal(6m, hours.AllocatedHours);
        Assert.Null(hours.AllocationPercent);
        Assert.Null(percentage.AllocatedHours);
        Assert.Equal(50m, percentage.AllocationPercent);
        Assert.Null(unknown.AllocatedHours);
        Assert.Null(unknown.AllocationPercent);
    }

    [Fact]
    public async Task MapBookingAsync_carries_the_resolved_staff_key_or_null()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var staffKey = Guid.NewGuid();

        var resolved = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b1" }, Guid.NewGuid(), staffKey, TestTenantId);
        var unresolved = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b2" }, Guid.NewGuid(), null, TestTenantId);

        Assert.Equal(staffKey, resolved.StaffKey);
        Assert.Null(unresolved.StaffKey);
    }

    [Fact]
    public async Task Re_mapping_the_same_booking_updates_the_existing_allocation()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository);
        var projectKey = Guid.NewGuid();

        var first = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b1", Title = "Old" }, projectKey, null, TestTenantId);
        var second = await sut.MapBookingAsync(new HubPlannerBookingDto { Id = "b1", Title = "New" }, projectKey, null, TestTenantId);

        Assert.Single(repository.PlannedAllocations);
        Assert.Equal(first.PlannedAllocationKey, second.PlannedAllocationKey);
        Assert.Equal("New", repository.PlannedAllocations[0].Title);
    }
}
