using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Cross-tenant leak tests for the Gold overview, mirroring
/// BrandingOps/BrandingThemeResolverServiceTenancyTests: tenant A's data
/// must never surface in tenant B's BuildOverviewAsync result.
/// </summary>
public class ProgrammeOverviewQueryServiceTenancyTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ProgrammeOverviewQueryService BuildSut(FakeProgrammeRepository repository, FakeStaffRepository staff) =>
        new(repository, staff, new FixedTimeProvider(Now));

    [Fact]
    public async Task BuildOverviewAsync_does_not_surface_another_tenants_programmes_or_work_items()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();

        var programmeA = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TenantA, Name = "Tenant A Drama", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var projectA = new Project { ProjectKey = Guid.NewGuid(), ProgrammeKey = programmeA.ProgrammeKey, TenantId = TenantA, Name = "Series 1", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var workstreamA = new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = projectA.ProjectKey, TenantId = TenantA, Name = "Post", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var workItemA = new WorkItem { WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstreamA.WorkstreamKey, TenantId = TenantA, Title = "Tenant A task", Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };

        repository.Programmes.Add(programmeA);
        repository.Projects.Add(projectA);
        repository.Workstreams.Add(workstreamA);
        repository.WorkItems.Add(workItemA);

        var sut = BuildSut(repository, staff);

        var tenantBOverview = await sut.BuildOverviewAsync(TenantB);

        Assert.Empty(tenantBOverview.WorkstreamStatuses);
        Assert.DoesNotContain(tenantBOverview.WorkstreamStatuses, w => w.WorkstreamName == "Post");
        Assert.Empty(tenantBOverview.ProgrammeHealth);

        var tenantAOverview = await sut.BuildOverviewAsync(TenantA);
        Assert.Single(tenantAOverview.WorkstreamStatuses);
        Assert.Single(tenantAOverview.ProgrammeHealth);
    }

    [Fact]
    public async Task BuildOverviewAsync_does_not_credit_another_tenants_staff_capacity()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();

        var staffA = new StaffProfile
        {
            StaffKey = Guid.NewGuid(),
            TenantId = TenantA,
            MemberId = 1,
            FullName = "Tenant A Person",
            Email = "person@tenant-a.example",
            IsActive = true,
            DefaultWorkHoursPerWeek = 37.5m,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };
        staff.Staff.Add(staffA);

        var workstreamA = new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = Guid.NewGuid(), TenantId = TenantA, Name = "Post", CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var workItemA = new WorkItem { WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstreamA.WorkstreamKey, TenantId = TenantA, Title = "Task", Stage = WorkItemLifecycleStage.InProgress, EstimatedHours = 10m, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        var allocationA = new WorkItemAllocation { AllocationKey = Guid.NewGuid(), WorkItemKey = workItemA.WorkItemKey, TenantId = TenantA, StaffKey = staffA.StaffKey, IsPrimary = true, CreatedAtUtc = Now.UtcDateTime };

        repository.Workstreams.Add(workstreamA);
        repository.WorkItems.Add(workItemA);
        repository.Allocations.Add(allocationA);

        var sut = BuildSut(repository, staff);

        var tenantBOverview = await sut.BuildOverviewAsync(TenantB);

        Assert.Empty(tenantBOverview.ResourceCapacity);
    }
}
