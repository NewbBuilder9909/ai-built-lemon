using System.Text.Json;
using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Models.Integrations.ClickUp.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Integrations.ClickUp;

namespace ProgrammePulse.Tests.Integrations;

public class ClickUpMappingServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ClickUpMappingService BuildSut(FakeProgrammeRepository repository, FakeStaffRepository staff, FakeIdentityResolutionRepository? identity = null)
    {
        var time = new FixedTimeProvider(Now);
        return new ClickUpMappingService(repository, new ProgrammePulse.Services.ProgrammeOps.StaffIdentityResolver(identity ?? new FakeIdentityResolutionRepository(), staff, time), new ClickUpStatusMapper(), time);
    }

    [Fact]
    public async Task MapWorkItemAsync_queues_an_assignee_nobody_can_match_instead_of_dropping_them_silently()
    {
        var repository = new FakeProgrammeRepository();
        var identity = new FakeIdentityResolutionRepository();
        var sut = BuildSut(repository, new FakeStaffRepository(), identity);
        var task = new ClickUpTaskDto
        {
            Id = "task-7",
            Name = "Orphan task",
            Status = new ClickUpStatusDto { Status = "in progress" },
            Assignees = [new ClickUpUserDto { Id = 555, Username = "contractor", Email = "contractor@agency.example" }]
        };

        var workItem = await sut.MapWorkItemAsync(task, Guid.NewGuid(), TestTenantId);

        Assert.Null(workItem.AssignedStaffKey);
        Assert.Empty(await repository.GetAllocationsByWorkItemKeyAsync(workItem.WorkItemKey, TestTenantId));
        var queued = Assert.Single(identity.Unresolved);
        Assert.Equal(("ClickUp", "555", "contractor@agency.example", "contractor", "task assignee"),
            (queued.ExternalSource, queued.ExternalUserId, queued.Email, queued.DisplayName, queued.Context));
    }

    /// <summary>An Admin-approved link for a ClickUp user, the only way a person is now attributed.</summary>
    private static FakeIdentityResolutionRepository Approved(params (long UserId, Guid StaffKey)[] links)
    {
        var identity = new FakeIdentityResolutionRepository();
        foreach (var (userId, staffKey) in links)
        {
            identity.Links.Add(new ExternalIdentityLink
            {
                LinkKey = Guid.NewGuid(), TenantId = TestTenantId, ExternalSource = "ClickUp",
                ExternalUserId = userId.ToString(), StaffKey = staffKey, CreatedAtUtc = Now.UtcDateTime
            });
        }

        return identity;
    }

    [Fact]
    public async Task MapProgrammeAsync_maps_space_name_and_external_id()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());

        var programme = await sut.MapProgrammeAsync(new ClickUpSpaceDto { Id = "space-1", Name = "Drama Productions" }, TestTenantId);

        Assert.Equal("Drama Productions", programme.Name);
        Assert.Equal("ClickUp", programme.ExternalSource);
        Assert.Equal("space-1", programme.ExternalId);
        Assert.Single(repository.Programmes);
    }

    [Fact]
    public async Task MapWorkItemAsync_maps_status_due_date_and_estimate()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());
        var workstreamKey = Guid.NewGuid();

        var dueDate = new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero);
        var task = new ClickUpTaskDto
        {
            Id = "task-1",
            Name = "Edit rough cut",
            Status = new ClickUpStatusDto { Status = "in progress" },
            DateDue = dueDate.ToUnixTimeMilliseconds().ToString(),
            TimeEstimateMs = 7_200_000 // 2 hours
        };

        var workItem = await sut.MapWorkItemAsync(task, workstreamKey, TestTenantId);

        Assert.Equal("Edit rough cut", workItem.Title);
        Assert.Equal(WorkItemLifecycleStage.InProgress, workItem.Stage);
        Assert.Equal("in progress", workItem.RawStatus);
        Assert.Equal(dueDate.UtcDateTime, workItem.DueDateUtc);
        Assert.Equal(2m, workItem.EstimatedHours);
        Assert.Equal(workstreamKey, workItem.WorkstreamKey);
    }

    [Fact]
    public async Task MapWorkItemAsync_leaves_an_email_match_unassigned_until_an_admin_approves_it()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var staffKey = Guid.NewGuid();
        staffRepository.Staff.Add(new StaffProfile
        {
            StaffKey = staffKey,
            TenantId = TestTenantId,
            MemberId = 1,
            FullName = "Jamie Gardner",
            Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });

        var identity = new FakeIdentityResolutionRepository();
        var sut = BuildSut(repository, staffRepository, identity);
        var task = new ClickUpTaskDto
        {
            Id = "task-2",
            Name = "Colour grade",
            Status = new ClickUpStatusDto { Status = "done" },
            Assignees = [new ClickUpUserDto { Id = 99, Email = "JAMIE@example.com" }]
        };

        // Anyone who can set an email in ClickUp could otherwise take Jamie's name.
        var workItem = await sut.MapWorkItemAsync(task, Guid.NewGuid(), TestTenantId);

        Assert.Null(workItem.AssignedStaffKey);
        Assert.Equal(staffKey, Assert.Single(identity.Unresolved).SuggestedStaffKey);

        // Once the suggestion is approved as a link, the next sync assigns it.
        var approved = await BuildSut(repository, staffRepository, Approved((99, staffKey))).MapWorkItemAsync(task, Guid.NewGuid(), TestTenantId);
        Assert.Equal(staffKey, approved.AssignedStaffKey);
    }

    [Fact]
    public async Task MapWorkItemAsync_with_multiple_assignees_creates_allocation_per_assignee_and_sets_first_as_primary()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var firstStaffKey = Guid.NewGuid();
        var secondStaffKey = Guid.NewGuid();
        staffRepository.Staff.Add(new StaffProfile
        {
            StaffKey = firstStaffKey,
            TenantId = TestTenantId,
            MemberId = 1,
            FullName = "Jamie Gardner",
            Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });
        staffRepository.Staff.Add(new StaffProfile
        {
            StaffKey = secondStaffKey,
            TenantId = TestTenantId,
            MemberId = 2,
            FullName = "Alex Producer",
            Email = "alex@example.com",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });

        var sut = BuildSut(repository, staffRepository, Approved((1, firstStaffKey), (2, secondStaffKey)));
        var task = new ClickUpTaskDto
        {
            Id = "task-4",
            Name = "Multi-assignee task",
            Status = new ClickUpStatusDto { Status = "in progress" },
            Assignees =
            [
                new ClickUpUserDto { Id = 1, Email = "jamie@example.com" },
                new ClickUpUserDto { Id = 2, Email = "alex@example.com" }
            ]
        };

        var workItem = await sut.MapWorkItemAsync(task, Guid.NewGuid(), TestTenantId);

        Assert.Equal(firstStaffKey, workItem.AssignedStaffKey);

        var allocations = await repository.GetAllocationsByWorkItemKeyAsync(workItem.WorkItemKey, TestTenantId);
        Assert.Equal(2, allocations.Count);
        Assert.Contains(allocations, a => a.StaffKey == firstStaffKey && a.IsPrimary);
        Assert.Contains(allocations, a => a.StaffKey == secondStaffKey && !a.IsPrimary);
    }

    [Fact]
    public async Task MapWorkItemAsync_reads_milestone_custom_field()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());

        var milestoneValue = JsonSerializer.SerializeToElement(true);
        var task = new ClickUpTaskDto
        {
            Id = "task-3",
            Name = "Delivery",
            Status = new ClickUpStatusDto { Status = "done" },
            CustomFields = [new ClickUpCustomFieldDto { Id = "cf-1", Name = "Milestone", Value = milestoneValue }]
        };

        var workItem = await sut.MapWorkItemAsync(task, Guid.NewGuid(), TestTenantId);

        Assert.True(workItem.IsMilestone);
    }

    [Fact]
    public async Task MapTimeEntryAsync_correlates_to_work_item_and_staff_and_converts_duration()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var staffKey = Guid.NewGuid();
        staffRepository.Staff.Add(new StaffProfile
        {
            StaffKey = staffKey,
            TenantId = TestTenantId,
            MemberId = 1,
            FullName = "Jamie Gardner",
            Email = "jamie@example.com",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });

        var sut = BuildSut(repository, staffRepository, Approved((1, staffKey)));
        var workItem = await sut.MapWorkItemAsync(new ClickUpTaskDto { Id = "task-9", Name = "Grade", Status = new ClickUpStatusDto { Status = "done" } }, Guid.NewGuid(), TestTenantId);

        var start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var entry = await sut.MapTimeEntryAsync(new ClickUpTimeEntryDto
        {
            Id = "entry-1",
            Task = new ClickUpTaskReferenceDto { Id = "task-9" },
            DurationMs = 5_400_000, // 1.5 hours
            StartMs = start.ToUnixTimeMilliseconds().ToString(),
            Billable = true,
            User = new ClickUpUserDto { Id = 1, Email = "JAMIE@example.com" }
        }, TestTenantId);

        Assert.Equal(workItem.WorkItemKey, entry.WorkItemKey);
        Assert.Equal(staffKey, entry.StaffKey);
        Assert.Equal(1.5m, entry.DurationHours);
        Assert.Equal(start.UtcDateTime, entry.StartedAtUtc);
        Assert.True(entry.IsBillable);
        Assert.Single(repository.TimeEntries);
    }

    [Fact]
    public async Task MapTimeEntryAsync_carries_the_billable_flag_through_when_false()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());

        var entry = await sut.MapTimeEntryAsync(new ClickUpTimeEntryDto
        {
            Id = "entry-3",
            DurationMs = 3_600_000,
            Billable = false
        }, TestTenantId);

        Assert.False(entry.IsBillable);
    }

    [Fact]
    public async Task MapTimeEntryAsync_leaves_work_item_and_staff_null_when_unresolvable()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());

        var entry = await sut.MapTimeEntryAsync(new ClickUpTimeEntryDto
        {
            Id = "entry-2",
            Task = new ClickUpTaskReferenceDto { Id = "unknown-task" },
            DurationMs = 3_600_000
        }, TestTenantId);

        Assert.Null(entry.WorkItemKey);
        Assert.Null(entry.StaffKey);
        Assert.Null(entry.StartedAtUtc);
    }

    [Fact]
    public async Task Re_mapping_the_same_external_id_updates_the_existing_row_instead_of_duplicating()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository());

        var first = await sut.MapProgrammeAsync(new ClickUpSpaceDto { Id = "space-1", Name = "Old Name" }, TestTenantId);
        var second = await sut.MapProgrammeAsync(new ClickUpSpaceDto { Id = "space-1", Name = "New Name" }, TestTenantId);

        Assert.Single(repository.Programmes);
        Assert.Equal(first.ProgrammeKey, second.ProgrammeKey);
        Assert.Equal("New Name", repository.Programmes[0].Name);
    }
}
