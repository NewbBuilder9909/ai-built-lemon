using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The query service half: tenant scoping, what counts as a context, and the
/// capability split. The name-withholding test matters most — it is the
/// difference between "the view hides names" and "the service never fetched
/// them", and CLAUDE.md requires the second for anything role-gated.
/// </summary>
public sealed class DeliveryLoadQueryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Wednesday; the ISO week it belongs to starts Monday 2026-09-21.
    // Wednesday of the week after CurrentWeek: the page assesses the last
    // complete week, so CurrentWeek is the one under assessment.
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CurrentWeek = new(2026, 9, 21);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Without_the_person_capability_no_names_are_returned_but_the_counts_still_are()
    {
        var (repository, staff, person) = SeedElevatedPerson();
        var sut = new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now));

        var withNames = await sut.BuildAsync(TenantId, includePeople: true);
        var withoutNames = await sut.BuildAsync(TenantId, includePeople: false);

        // The finding exists either way — the team-level reader is told that
        // one person is elevated, and only that.
        Assert.Equal(1, withNames.ElevatedCount);
        Assert.Equal(1, withoutNames.ElevatedCount);

        Assert.Equal("Rhian Davies", Assert.Single(withNames.Findings).Name);
        Assert.Empty(withoutNames.Findings);
        Assert.False(withoutNames.IncludesPeople);
        Assert.Equal(person, Assert.Single(withNames.Findings).StaffKey);
    }

    /// <summary>
    /// A Board member with no time and no assigned work was reported as "a
    /// gap in what this page can see". They are outside delivery by role, so
    /// they are counted separately rather than inflating the gap.
    /// </summary>
    [Fact]
    public async Task Oversight_only_members_without_work_are_counted_separately_not_as_a_data_gap()
    {
        var (repository, staff, _) = SeedElevatedPerson();
        var boardKey = AddStaff(staff, TenantId, "Blair Board");
        var boardMemberId = staff.Staff.Single(s => s.StaffKey == boardKey).MemberId;

        var report = await new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now), new FakeDeliveryRoleDirectory(boardMemberId))
            .BuildAsync(TenantId, includePeople: true);

        Assert.Equal(1, report.PeopleInScope);
        Assert.Equal(0, report.PeopleWithoutTimeData);
        Assert.Equal(1, report.PeopleInOversightRolesOnly);
    }

    [Fact]
    public async Task A_Monday_before_anyone_has_logged_the_new_week_is_not_read_as_logging_stopped()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        var person = AddStaff(staff, TenantId, "Regular Rhys");
        var item = AddWorkItem(repository, TenantId, AddProject(repository, TenantId, "Series 4"));

        // Logged every week up to and including last week; nothing yet this week.
        for (var weeksAgo = 0; weeksAgo <= 6; weeksAgo++)
            AddTimeEntry(repository, TenantId, person, item, CurrentWeek.AddDays(-7 * weeksAgo));
        var monday = new DateTimeOffset(CurrentWeek.AddDays(7).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

        var report = await new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(monday))
            .BuildAsync(TenantId, includePeople: true);

        Assert.DoesNotContain(report.Findings, f => f.Signal == LoadSignal.CoverageFellAway);
        Assert.Equal(CurrentWeek, report.CurrentWeekStart);
    }

    [Fact]
    public async Task Another_tenants_work_never_reaches_this_tenants_figures()
    {
        var (repository, staff, _) = SeedElevatedPerson();

        // Same shape of data, different tenant.
        SeedElevatedPersonInto(repository, staff, OtherTenantId, "Someone Else");

        var sut = new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now));
        var report = await sut.BuildAsync(TenantId, includePeople: true);

        Assert.Equal(1, report.PeopleInScope);
        Assert.Equal("Rhian Davies", Assert.Single(report.Findings).Name);
    }

    [Fact]
    public async Task A_context_is_a_project_so_many_items_in_one_project_are_not_fragmentation()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        var person = AddStaff(staff, TenantId, "Single Project Sam");

        // One project, eight work items, logged every week. A person who
        // counted work items would look badly fragmented; counting projects
        // shows the truth, which is that they are on one thing.
        var project = AddProject(repository, TenantId, "Series 4");
        var items = Enumerable.Range(0, 8).Select(_ => AddWorkItem(repository, TenantId, project)).ToList();

        for (var weeksAgo = 0; weeksAgo <= 6; weeksAgo++)
        {
            foreach (var item in items)
            {
                AddTimeEntry(repository, TenantId, person, item, CurrentWeek.AddDays(-7 * weeksAgo));
            }
        }

        var sut = new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now));
        var report = await sut.BuildAsync(TenantId, includePeople: true);

        Assert.Empty(report.Findings);
        Assert.Equal(1, report.PeopleWithSufficientHistory);
    }

    [Fact]
    public async Task Time_entries_with_no_work_date_are_left_out_rather_than_dated_to_now()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        var person = AddStaff(staff, TenantId, "Undated Uma");
        var project = AddProject(repository, TenantId, "Series 4");
        var item = AddWorkItem(repository, TenantId, project);

        AddTimeEntry(repository, TenantId, person, item, workDate: null);

        var sut = new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now));
        var report = await sut.BuildAsync(TenantId, includePeople: true);

        // Undated effort is real but unplaceable, so it neither creates a
        // week nor pads the current one.
        Assert.Equal(1, report.PeopleWithoutTimeData);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Inactive_people_are_out_of_scope_entirely()
    {
        var (repository, staff, person) = SeedElevatedPerson();
        var existing = staff.Staff.Single(s => s.StaffKey == person);
        staff.Staff.Remove(existing);
        staff.Staff.Add(existing with { IsActive = false });

        var sut = new DeliveryLoadQueryService(repository, staff, new FixedTimeProvider(Now));
        var report = await sut.BuildAsync(TenantId, includePeople: true);

        Assert.Equal(0, report.PeopleInScope);
        Assert.Empty(report.Findings);
    }

    /// <summary>Five weeks of two projects, then a current week of five.</summary>
    private static (FakeProgrammeRepository Repository, FakeStaffRepository Staff, Guid StaffKey) SeedElevatedPerson()
    {
        var repository = new FakeProgrammeRepository();
        var staff = new FakeStaffRepository();
        var person = SeedElevatedPersonInto(repository, staff, TenantId, "Rhian Davies");
        return (repository, staff, person);
    }

    private static Guid SeedElevatedPersonInto(
        FakeProgrammeRepository repository, FakeStaffRepository staff, Guid tenantId, string name)
    {
        var person = AddStaff(staff, tenantId, name);

        var baselineProjects = Enumerable.Range(0, 2)
            .Select(i => AddWorkItem(repository, tenantId, AddProject(repository, tenantId, $"Baseline {i}")))
            .ToList();

        for (var weeksAgo = 1; weeksAgo <= 5; weeksAgo++)
        {
            foreach (var item in baselineProjects)
            {
                AddTimeEntry(repository, tenantId, person, item, CurrentWeek.AddDays(-7 * weeksAgo));
            }
        }

        foreach (var i in Enumerable.Range(0, 5))
        {
            var item = AddWorkItem(repository, tenantId, AddProject(repository, tenantId, $"Current {i}"));
            AddTimeEntry(repository, tenantId, person, item, CurrentWeek);
        }

        return person;
    }

    private static Guid AddStaff(FakeStaffRepository staff, Guid tenantId, string name)
    {
        var key = Guid.NewGuid();
        staff.Staff.Add(new StaffProfile
        {
            StaffKey = key,
            MemberId = Random.Shared.Next(1, 100_000),
            FullName = name,
            Email = $"{key:N}@northstar.test",
            TenantId = tenantId,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });
        return key;
    }

    private static Guid AddProject(FakeProgrammeRepository repository, Guid tenantId, string name)
    {
        var programmeKey = Guid.NewGuid();
        var projectKey = Guid.NewGuid();
        repository.Programmes.Add(new Programme
        {
            ProgrammeKey = programmeKey, Name = $"{name} programme", TenantId = tenantId,
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        repository.Projects.Add(new Project
        {
            ProjectKey = projectKey, ProgrammeKey = programmeKey, Name = name, TenantId = tenantId,
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        return projectKey;
    }

    private static Guid AddWorkItem(FakeProgrammeRepository repository, Guid tenantId, Guid projectKey)
    {
        var workstreamKey = Guid.NewGuid();
        var workItemKey = Guid.NewGuid();
        repository.Workstreams.Add(new Workstream
        {
            WorkstreamKey = workstreamKey, ProjectKey = projectKey, Name = "Delivery", TenantId = tenantId,
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        repository.WorkItems.Add(new WorkItem
        {
            WorkItemKey = workItemKey, WorkstreamKey = workstreamKey, Title = "Task",
            Stage = WorkItemLifecycleStage.InProgress, TenantId = tenantId,
            CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime
        });
        return workItemKey;
    }

    private static void AddTimeEntry(
        FakeProgrammeRepository repository, Guid tenantId, Guid staffKey, Guid workItemKey, DateOnly? workDate)
    {
        repository.TimeEntries.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(),
            TenantId = tenantId,
            WorkItemKey = workItemKey,
            StaffKey = staffKey,
            DurationHours = 4m,
            WorkDate = workDate,
            ExternalSource = "clickup",
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });
    }
}
