using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class ReportingQueryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

    private sealed class FakeStaffRateRepository : IStaffRateRepository
    {
        public readonly List<StaffRate> Rates = [];

        public Task<StaffRate?> GetCurrentAsync(Guid staffKey) =>
            Task.FromResult(Rates.Where(r => r.StaffKey == staffKey && r.EffectiveToUtc is null).OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefault());

        public Task<IReadOnlyList<StaffRate>> GetHistoryAsync(Guid staffKey) =>
            Task.FromResult<IReadOnlyList<StaffRate>>(Rates.Where(r => r.StaffKey == staffKey).ToList());

        public Task<StaffRate> SetCurrentRateAsync(Guid staffKey, decimal costPerHour, string rateCurrency, Guid changedByStaffKey) =>
            throw new NotSupportedException("Not needed for these tests.");
    }

    private sealed class FakeWorkHoursHistoryRepository : IWorkHoursHistoryRepository
    {
        public readonly List<WorkHoursHistory> Rows = [];

        public Task<WorkHoursHistory?> GetCurrentAsync(Guid staffKey) =>
            Task.FromResult(Rows.Where(r => r.StaffKey == staffKey && r.EffectiveToUtc is null).OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefault());

        public Task<IReadOnlyList<WorkHoursHistory>> GetHistoryAsync(Guid staffKey) =>
            Task.FromResult<IReadOnlyList<WorkHoursHistory>>(Rows.Where(r => r.StaffKey == staffKey).ToList());

        public Task<WorkHoursHistory> SetCurrentHoursAsync(Guid staffKey, decimal hoursPerWeek, Guid changedByStaffKey) =>
            throw new NotSupportedException("Not needed for these tests.");
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ReportingQueryService BuildSut(
        FakeProgrammeRepository repository,
        FakeStaffRepository staff,
        FakeAvailabilityRepository availability,
        FakeStaffRateRepository rates,
        FakeWorkHoursHistoryRepository? workHours = null,
        IDeliveryRoleDirectory? deliveryRoles = null) =>
        new(repository, staff, availability, rates, workHours ?? new FakeWorkHoursHistoryRepository(), new ProgrammeOverviewQueryService(repository, staff, new FixedTimeProvider(Now)), new FixedTimeProvider(Now), deliveryRoles);

    private static StaffProfile MakeStaff(string? team = "Drama", decimal hoursPerWeek = 35m) => new()
    {
        StaffKey = Guid.NewGuid(),
        MemberId = 1,
        FullName = "Jamie Gardner",
        Email = "jamie@example.com",
        Team = team,
        IsActive = true,
        DefaultWorkHoursPerWeek = hoursPerWeek,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    private static (Programme Programme, Project Project, Workstream Workstream) SeedHierarchy(FakeProgrammeRepository repository, Guid? customerKey = null)
    {
        var programme = new Programme
        {
            ProgrammeKey = Guid.NewGuid(),
            Name = "Drama",
            CustomerKey = customerKey,
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

    private static WorkItem MakeWorkItem(Guid workstreamKey, decimal? estimatedHours = null, Guid? assignedStaffKey = null) => new()
    {
        WorkItemKey = Guid.NewGuid(),
        WorkstreamKey = workstreamKey,
        Title = "Edit",
        Stage = WorkItemLifecycleStage.InProgress,
        AssignedStaffKey = assignedStaffKey,
        EstimatedHours = estimatedHours,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    private static TimeEntry MakeTimeEntry(Guid? workItemKey, Guid? staffKey, decimal hours, DateTime? startedAtUtc,
        bool isBillable = true, DateOnly? workDate = null, bool billabilityKnown = true) => new()
    {
        TimeEntryKey = Guid.NewGuid(),
        WorkItemKey = workItemKey,
        StaffKey = staffKey,
        DurationHours = hours,
        StartedAtUtc = startedAtUtc,
        WorkDate = workDate,
        IsBillable = isBillable,
        BillabilityKnown = billabilityKnown,
        TenantId = TestTenantId,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    [Theory]
    [InlineData(null, 3)]          // tenant-wide: an Admin
    [InlineData("Drama", 2)]       // own team, trimmed and case-insensitive below
    [InlineData("", 0)]            // no team: nobody, never everybody
    [InlineData("   ", 0)]
    public async Task A_blank_team_scope_shows_no_named_contributors_rather_than_all_of_them(string? teamFilter, int expected)
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(MakeStaff("Drama"));
        staffRepository.Staff.Add(MakeStaff(" drama "));
        staffRepository.Staff.Add(MakeStaff(null));

        var hub = await BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository())
            .BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), teamFilter, TestTenantId);

        Assert.Equal(expected, hub.ContributorCapacity.Count);
    }

    [Theory]
    [InlineData(true, "Drama", null)]
    [InlineData(false, "Drama", "Drama")]
    [InlineData(false, " Drama ", "Drama")]
    [InlineData(false, null, "")]
    [InlineData(false, "  ", "")]
    public void Only_a_tenant_wide_reader_gets_the_unscoped_view(bool tenantWide, string? team, string? expected)
    {
        Assert.Equal(expected, ReportingTeamScope.For(tenantWide, team));
    }

    private static StaffProfile MakeNamedStaff(string name, int memberId) => MakeStaff() with { FullName = name, MemberId = memberId, StaffKey = Guid.NewGuid() };

    /// <summary>
    /// Board, Analyst and Platform Admin oversee delivery. Counting one at 0%
    /// dragged team utilisation down — the demo's 24% was half made of people
    /// who were never meant to log time.
    /// </summary>
    [Fact]
    public async Task Oversight_only_members_with_no_time_are_left_out_of_capacity_and_named()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var delivery = MakeNamedStaff("Alex Delivery", memberId: 10);
        var board = MakeNamedStaff("Blair Board", memberId: 20);
        staffRepository.Staff.AddRange([delivery, board]);
        repository.TimeEntries.Add(MakeTimeEntry(null, delivery.StaffKey, 7m, null, workDate: new DateOnly(2026, 9, 11)));

        var model = await BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository(),
                deliveryRoles: new FakeDeliveryRoleDirectory(20))
            .BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), null, TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        Assert.Equal("Alex Delivery", row.StaffFullName);
        Assert.Equal(1, Assert.Single(model.TeamCapacitySubtotals).ContributorCount);
        Assert.Equal(["Blair Board"], model.ExcludedOversightStaff);
    }

    /// <summary>Role is not evidence: an analyst who logs project time has real hours, so they count.</summary>
    [Fact]
    public async Task Recorded_time_brings_an_oversight_role_back_into_capacity()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var analyst = MakeNamedStaff("Ana Lyst", memberId: 30);
        staffRepository.Staff.Add(analyst);
        repository.TimeEntries.Add(MakeTimeEntry(null, analyst.StaffKey, 2m, null, workDate: new DateOnly(2026, 9, 11)));

        var model = await BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository(),
                deliveryRoles: new FakeDeliveryRoleDirectory(30))
            .BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), null, TestTenantId);

        Assert.Equal("Ana Lyst", Assert.Single(model.ContributorCapacity).StaffFullName);
        Assert.Empty(model.ExcludedOversightStaff!);
    }

    /// <summary>
    /// The exclusion is for oversight roles only. A delivery-role member with
    /// no time still shows at 0% — that gap is real and must stay visible.
    /// </summary>
    [Fact]
    public async Task A_delivery_role_member_with_no_time_still_counts_at_zero()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var quiet = MakeNamedStaff("Quinn Quiet", memberId: 40);
        staffRepository.Staff.Add(quiet);

        var model = await BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository(),
                deliveryRoles: new FakeDeliveryRoleDirectory(/* nobody is oversight-only */))
            .BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), null, TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        Assert.Equal(0, row.UtilisationPercent);
        Assert.Empty(model.ExcludedOversightStaff!);
    }

    [Fact]
    public async Task Source_work_date_counts_Tempo_effort_in_its_day_without_claiming_a_UTC_start()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var person = MakeStaff();
        staffRepository.Staff.Add(person);
        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 3m, null,
            isBillable: false, workDate: new DateOnly(2026, 9, 11), billabilityKnown: false));

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var inPeriod = await sut.BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), null, TestTenantId);
        var outOfPeriod = await sut.BuildHubAsync(new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 12), null, TestTenantId);

        Assert.Equal(3m, Assert.Single(inPeriod.ContributorCapacity).LoggedHours);
        Assert.Equal(3m, inPeriod.UnknownBillabilityHours);
        Assert.Equal(0, Assert.Single(inPeriod.ContributorCapacity).BillableUtilisationPercent);
        Assert.Equal(0m, Assert.Single(outOfPeriod.ContributorCapacity).LoggedHours);
    }

    [Fact]
    public async Task Unlinked_time_is_visible_with_source_identity_and_is_excluded_from_project_variance()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var item = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(item);
        repository.TimeEntries.Add(MakeTimeEntry(item.WorkItemKey, null, 2m, null,
            workDate: new DateOnly(2026, 9, 11)));
        repository.TimeEntries.Add(MakeTimeEntry(null, null, 3m, null,
            workDate: new DateOnly(2026, 9, 11)) with { ExternalSource = "Tempo", ExternalId = "site:42" });

        var model = await BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository())
            .BuildHubAsync(new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), null, TestTenantId);

        var unlinked = Assert.Single(model.UnlinkedTime);
        Assert.Equal(("Tempo", "site:42", 3m), (unlinked.Source, unlinked.ExternalId, unlinked.DurationHours));
        Assert.True(model.HasTempoDeletionCoverageGap);
        Assert.Equal(2m, Assert.Single(model.EffortVariance).ActualHours);
    }

    [Fact]
    public async Task BuildHubAsync_computes_effort_variance_as_actual_minus_estimated()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var item = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(item);
        repository.TimeEntries.Add(MakeTimeEntry(item.WorkItemKey, null, 4m, Now.UtcDateTime));
        repository.TimeEntries.Add(MakeTimeEntry(item.WorkItemKey, null, 3m, Now.UtcDateTime));

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);

        var row = Assert.Single(model.EffortVariance);
        Assert.Equal(10m, row.EstimatedHours);
        Assert.Equal(7m, row.ActualHours);
        Assert.Equal(-3m, row.VarianceHours);
        Assert.Equal(100, row.EstimateCoveragePercent);
    }

    [Fact]
    public async Task BuildHubAsync_exposes_incomplete_estimates_next_to_precise_looking_variance()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var estimated = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(estimated);
        repository.WorkItems.Add(MakeWorkItem(workstream.WorkstreamKey));
        repository.TimeEntries.Add(MakeTimeEntry(estimated.WorkItemKey, null, 12m, Now.UtcDateTime));

        var model = await BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository())
            .BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);

        var row = Assert.Single(model.EffortVariance);
        Assert.Equal(2, row.WorkItemCount);
        Assert.Equal(1, row.WorkItemsWithoutEstimate);
        Assert.Equal(50, row.EstimateCoveragePercent);
        Assert.Equal(2m, row.VarianceHours);
    }

    [Fact]
    public async Task BuildHubAsync_computes_residual_capacity_from_baseline_minus_leave_minus_logged()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var availability = new FakeAvailabilityRepository();

        // 35 hrs/wk => 7 hrs/day. A 5-weekday period (Mon-Fri) = 35 baseline hours.
        var monday = new DateOnly(2026, 9, 7);
        var friday = new DateOnly(2026, 9, 11);
        var person = MakeStaff(hoursPerWeek: 35m);
        staffRepository.Staff.Add(person);

        availability.Rows.Add(new Availability
        {
            StaffKey = person.StaffKey,
            Date = new DateOnly(2026, 9, 9),
            StartTime = TimeOnly.MinValue,
            EndTime = TimeOnly.MaxValue,
            Status = AvailabilityStatus.Holiday,
            Source = AvailabilitySource.LeavePolicy
        });

        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 10m, new DateTime(2026, 9, 8)));

        var sut = BuildSut(repository, staffRepository, availability, new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(monday, friday, null, TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        Assert.Equal(35m, row.BaselineHours);
        Assert.Equal(7m, row.LeaveHours);
        Assert.Equal(10m, row.LoggedHours);
        Assert.Equal(18m, row.ResidualCapacityHours); // 35 - 7 - 10
    }

    [Fact]
    public async Task BuildHubAsync_uses_the_hours_effective_at_the_time_not_todays_value_for_a_past_period()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var workHours = new FakeWorkHoursHistoryRepository();

        // Person is 40 hrs/wk *today* (StaffProfile.DefaultWorkHoursPerWeek —
        // what a pre-history/buggy calculation would use for any period), but
        // was only 20 hrs/wk during the queried week, per WorkHoursHistory:
        // closed row 20 hrs/wk up to 2026-09-01, open row 40 hrs/wk from then.
        var person = MakeStaff(hoursPerWeek: 40m);
        staffRepository.Staff.Add(person);
        workHours.Rows.Add(new WorkHoursHistory
        {
            StaffKey = person.StaffKey,
            HoursPerWeek = 20m,
            EffectiveFromUtc = new DateTime(2026, 1, 1),
            EffectiveToUtc = new DateTime(2026, 9, 1),
            ChangedByStaffKey = person.StaffKey,
            ChangedAtUtc = new DateTime(2026, 1, 1)
        });
        workHours.Rows.Add(new WorkHoursHistory
        {
            StaffKey = person.StaffKey,
            HoursPerWeek = 40m,
            EffectiveFromUtc = new DateTime(2026, 9, 1),
            EffectiveToUtc = null,
            ChangedByStaffKey = person.StaffKey,
            ChangedAtUtc = new DateTime(2026, 9, 1)
        });

        // A full Mon-Fri week entirely before the 2026-09-01 change.
        var monday = new DateOnly(2026, 8, 24);
        var friday = new DateOnly(2026, 8, 28);

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository(), workHours);
        var model = await sut.BuildHubAsync(monday, friday, null, TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        // 5 weekdays x (20/5) = 20, not 5 x (40/5) = 40 — the bug this fixes.
        Assert.Equal(20m, row.BaselineHours);
    }

    [Fact]
    public async Task BuildHubAsync_computes_billable_utilisation_separately_from_blended_utilisation()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var availability = new FakeAvailabilityRepository();

        // 35 hrs/wk over a 5-weekday period (Mon-Fri) = 35 baseline hours,
        // no leave, so available hours == baseline hours here.
        var monday = new DateOnly(2026, 9, 7);
        var friday = new DateOnly(2026, 9, 11);
        var person = MakeStaff(hoursPerWeek: 35m);
        staffRepository.Staff.Add(person);

        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 21m, new DateTime(2026, 9, 8), isBillable: true));
        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 14m, new DateTime(2026, 9, 9), isBillable: false));

        var sut = BuildSut(repository, staffRepository, availability, new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(monday, friday, null, TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        Assert.Equal(35m, row.LoggedHours); // 21 + 14, blended
        Assert.Equal(100, row.UtilisationPercent); // (21+14)/35
        Assert.Equal(60, row.BillableUtilisationPercent); // 21/35, billable only
    }

    [Fact]
    public async Task BuildHubAsync_scopes_contributor_capacity_by_team_filter()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        staffRepository.Staff.Add(MakeStaff(team: "Drama"));
        staffRepository.Staff.Add(MakeStaff(team: "Sport"));

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), "Drama", TestTenantId);

        var row = Assert.Single(model.ContributorCapacity);
        Assert.Equal("Drama", row.Team);
    }

    [Fact]
    public async Task BuildHubAsync_subtotals_contributor_capacity_by_team()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();

        // Two Drama contributors (35 hrs/wk each => 70 combined baseline over
        // the 5-weekday period) and one Sport contributor, no team.
        var dramaOne = MakeStaff(team: "Drama", hoursPerWeek: 35m);
        var dramaTwo = MakeStaff(team: "Drama", hoursPerWeek: 35m);
        var sport = MakeStaff(team: "Sport", hoursPerWeek: 35m);
        var noTeam = MakeStaff(team: null, hoursPerWeek: 35m);
        staffRepository.Staff.Add(dramaOne);
        staffRepository.Staff.Add(dramaTwo);
        staffRepository.Staff.Add(sport);
        staffRepository.Staff.Add(noTeam);

        repository.TimeEntries.Add(MakeTimeEntry(null, dramaOne.StaffKey, 10m, new DateTime(2026, 9, 8)));
        repository.TimeEntries.Add(MakeTimeEntry(null, dramaTwo.StaffKey, 20m, new DateTime(2026, 9, 8)));

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, TestTenantId);

        Assert.Equal(3, model.TeamCapacitySubtotals.Count); // Drama, Sport, (No team)
        var drama = model.TeamCapacitySubtotals.Single(t => t.TeamName == "Drama");
        Assert.Equal(2, drama.ContributorCount);
        Assert.Equal(70m, drama.BaselineHours); // 35 + 35
        Assert.Equal(30m, drama.LoggedHours); // 10 + 20
        Assert.Equal(40m, drama.ResidualCapacityHours); // 70 - 30
        Assert.Equal(43, drama.UtilisationPercent); // 30/70

        var noTeamRow = Assert.Single(model.TeamCapacitySubtotals, t => t.TeamName == "(No team)");
        Assert.Equal(1, noTeamRow.ContributorCount);
    }

    [Fact]
    public async Task BuildHubAsync_rolls_programmes_up_by_customer_including_no_customer_bucket()
    {
        var repository = new FakeProgrammeRepository();
        var customer = new Customer { CustomerKey = Guid.NewGuid(), Name = "Acme Broadcasting", TenantId = TestTenantId, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        repository.Customers.Add(customer);

        SeedHierarchy(repository, customerKey: customer.CustomerKey);
        SeedHierarchy(repository); // no customer assigned

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, TestTenantId);

        Assert.Contains(model.CustomerRollup, r => r.CustomerName == "Acme Broadcasting" && r.ProgrammeCount == 1);
        Assert.Contains(model.CustomerRollup, r => r.CustomerName == "(no customer)" && r.ProgrammeCount == 1);
    }

    [Fact]
    public async Task BuildHubAsync_never_includes_cost_or_rate_fields()
    {
        var repository = new FakeProgrammeRepository();
        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, TestTenantId);

        static bool LooksLikeCostOrRateField(string name) =>
            name.Contains("Cost", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Rate", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Rate", StringComparison.OrdinalIgnoreCase);

        foreach (var type in new[] { model.GetType(), typeof(ProgrammePulse.Models.ViewModels.Reporting.ContributorCapacityRowViewModel), typeof(ProgrammePulse.Models.ViewModels.Reporting.EffortVarianceRowViewModel) })
        {
            Assert.DoesNotContain(type.GetProperties(), p => LooksLikeCostOrRateField(p.Name));
        }
    }

    [Fact]
    public async Task BuildCostSummaryAsync_costs_entries_using_the_rate_effective_on_the_entry_date()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var rates = new FakeStaffRateRepository();

        var person = MakeStaff();
        staffRepository.Staff.Add(person);

        rates.Rates.Add(new StaffRate
        {
            StaffKey = person.StaffKey,
            CostPerHour = 20m,
            RateCurrency = "GBP",
            EffectiveFromUtc = new DateTime(2026, 1, 1),
            EffectiveToUtc = new DateTime(2026, 6, 1),
            ChangedByStaffKey = person.StaffKey,
            ChangedAtUtc = new DateTime(2026, 1, 1)
        });
        rates.Rates.Add(new StaffRate
        {
            StaffKey = person.StaffKey,
            CostPerHour = 25m,
            RateCurrency = "GBP",
            EffectiveFromUtc = new DateTime(2026, 6, 1),
            EffectiveToUtc = null,
            ChangedByStaffKey = person.StaffKey,
            ChangedAtUtc = new DateTime(2026, 6, 1)
        });

        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 5m, new DateTime(2026, 3, 1))); // 20/hr -> 100
        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 2m, new DateTime(2026, 7, 1))); // 25/hr -> 50

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), rates);
        var summary = await sut.BuildCostSummaryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestTenantId);

        var row = Assert.Single(summary.Rows);
        Assert.Equal(7m, row.LoggedHours);
        Assert.Equal(150m, row.TotalCost);
        Assert.Equal(150m, summary.GrandTotalCost);
        Assert.Equal(0m, summary.UnpricedEntryHours);
    }

    [Fact]
    public async Task BuildCostSummaryAsync_splits_billable_from_non_billable_hours()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var rates = new FakeStaffRateRepository();

        var person = MakeStaff();
        staffRepository.Staff.Add(person);
        rates.Rates.Add(new StaffRate
        {
            StaffKey = person.StaffKey,
            CostPerHour = 20m,
            RateCurrency = "GBP",
            EffectiveFromUtc = new DateTime(2026, 1, 1),
            ChangedByStaffKey = person.StaffKey,
            ChangedAtUtc = new DateTime(2026, 1, 1)
        });

        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 5m, new DateTime(2026, 3, 1), isBillable: true));
        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 2m, new DateTime(2026, 3, 2), isBillable: false));

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), rates);
        var summary = await sut.BuildCostSummaryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestTenantId);

        var row = Assert.Single(summary.Rows);
        Assert.Equal(7m, row.LoggedHours);
        Assert.Equal(5m, row.BillableHours);
        Assert.Equal(140m, row.TotalCost);
    }

    [Fact]
    public async Task BuildCostSummaryAsync_excludes_entries_with_no_effective_rate_from_the_total()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var person = MakeStaff();
        staffRepository.Staff.Add(person);

        // No StaffRate rows at all for this staff member.
        repository.TimeEntries.Add(MakeTimeEntry(null, person.StaffKey, 4m, new DateTime(2026, 3, 1)));

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var summary = await sut.BuildCostSummaryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestTenantId);

        Assert.Empty(summary.Rows);
        Assert.Equal(0m, summary.GrandTotalCost);
        Assert.Equal(4m, summary.UnpricedEntryHours);
    }

    [Fact]
    public async Task BuildCostSummaryAsync_counts_only_dated_unmapped_hours_in_the_selected_period()
    {
        var repository = new FakeProgrammeRepository();
        repository.TimeEntries.Add(MakeTimeEntry(null, null, 3m, new DateTime(2026, 3, 1)));
        repository.TimeEntries.Add(MakeTimeEntry(null, null, 7m, new DateTime(2026, 4, 1)));
        repository.TimeEntries.Add(MakeTimeEntry(null, null, 9m, null));

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var summary = await sut.BuildCostSummaryAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), TestTenantId);

        Assert.Equal(3m, summary.UnpricedEntryHours);
        Assert.Equal(9m, summary.UndatedTimeEntryHours);
    }
    [Fact]
    public async Task BuildCostSummaryAsync_breaks_totals_out_by_currency_when_staff_rates_differ()
    {
        var repository = new FakeProgrammeRepository();
        var staffRepository = new FakeStaffRepository();
        var rates = new FakeStaffRateRepository();

        var gbpStaff = MakeStaff();
        var usdStaff = MakeStaff();
        staffRepository.Staff.Add(gbpStaff);
        staffRepository.Staff.Add(usdStaff);

        rates.Rates.Add(new StaffRate
        {
            StaffKey = gbpStaff.StaffKey,
            CostPerHour = 20m,
            RateCurrency = "GBP",
            EffectiveFromUtc = new DateTime(2026, 1, 1),
            ChangedByStaffKey = gbpStaff.StaffKey,
            ChangedAtUtc = new DateTime(2026, 1, 1)
        });
        rates.Rates.Add(new StaffRate
        {
            StaffKey = usdStaff.StaffKey,
            CostPerHour = 30m,
            RateCurrency = "USD",
            EffectiveFromUtc = new DateTime(2026, 1, 1),
            ChangedByStaffKey = usdStaff.StaffKey,
            ChangedAtUtc = new DateTime(2026, 1, 1)
        });

        repository.TimeEntries.Add(MakeTimeEntry(null, gbpStaff.StaffKey, 5m, new DateTime(2026, 3, 1))); // 20/hr -> 100 GBP
        repository.TimeEntries.Add(MakeTimeEntry(null, usdStaff.StaffKey, 2m, new DateTime(2026, 3, 1))); // 30/hr -> 60 USD

        var sut = BuildSut(repository, staffRepository, new FakeAvailabilityRepository(), rates);
        var summary = await sut.BuildCostSummaryAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestTenantId);

        Assert.Equal(2, summary.TotalsByCurrency.Count);
        Assert.Contains(summary.TotalsByCurrency, t => t.Currency == "GBP" && t.TotalCost == 100m);
        Assert.Contains(summary.TotalsByCurrency, t => t.Currency == "USD" && t.TotalCost == 60m);
    }

    [Fact]
    public async Task BuildHubAsync_reports_baseline_variance_once_a_baseline_is_locked()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var item = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(item);
        repository.TimeEntries.Add(MakeTimeEntry(item.WorkItemKey, null, 12m, Now.UtcDateTime));

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());

        var beforeLock = await sut.BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);
        var rowBeforeLock = Assert.Single(beforeLock.EffortVariance);
        Assert.Null(rowBeforeLock.BaselineHours);
        Assert.Null(rowBeforeLock.BaselineVarianceHours);

        await repository.LockBaselineAsync(workstream.WorkstreamKey, 8m, null, Now.UtcDateTime, TestTenantId);
        // Estimate drifts after the baseline is locked — baseline variance must not follow it.
        item = item with { EstimatedHours = 20m };
        repository.WorkItems.Clear();
        repository.WorkItems.Add(item);

        var afterLock = await sut.BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);
        var rowAfterLock = Assert.Single(afterLock.EffortVariance);
        Assert.Equal(8m, rowAfterLock.BaselineHours);
        Assert.Equal(4m, rowAfterLock.BaselineVarianceHours); // 12 actual - 8 baseline
        Assert.Equal(-8m, rowAfterLock.VarianceHours); // 12 actual - 20 (drifted) estimate — unaffected by the baseline
    }

    [Fact]
    public async Task BuildHubAsync_does_not_infer_earned_value_or_forecast_from_closed_task_counts()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        // 2 items, 10 hrs baseline each side (20 total): 1 closed (Done), 1 still in progress.
        var doneItem = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m) with { Stage = WorkItemLifecycleStage.Done };
        var openItem = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(doneItem);
        repository.WorkItems.Add(openItem);
        repository.TimeEntries.Add(MakeTimeEntry(doneItem.WorkItemKey, null, 12m, Now.UtcDateTime)); // actual so far

        await repository.LockBaselineAsync(workstream.WorkstreamKey, 20m, null, Now.UtcDateTime, TestTenantId);

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);

        var row = Assert.Single(model.EffortVariance);
        Assert.Null(row.ForecastAtCompletionHours);
        Assert.Null(row.ForecastVarianceHours);
    }

    [Fact]
    public async Task BuildHubAsync_leaves_forecast_null_when_no_item_is_closed_yet()
    {
        var repository = new FakeProgrammeRepository();
        var (_, _, workstream) = SeedHierarchy(repository);
        var item = MakeWorkItem(workstream.WorkstreamKey, estimatedHours: 10m);
        repository.WorkItems.Add(item);
        repository.TimeEntries.Add(MakeTimeEntry(item.WorkItemKey, null, 4m, Now.UtcDateTime));
        await repository.LockBaselineAsync(workstream.WorkstreamKey, 10m, null, Now.UtcDateTime, TestTenantId);

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime), null, TestTenantId);

        var row = Assert.Single(model.EffortVariance);
        Assert.Null(row.ForecastAtCompletionHours);
        Assert.Null(row.ForecastVarianceHours);
    }

    [Fact]
    public async Task BuildHubAsync_scopes_effort_variance_by_programme_filter()
    {
        var repository = new FakeProgrammeRepository();
        var (programmeA, _, workstreamA) = SeedHierarchy(repository);
        var (programmeB, _, workstreamB) = SeedHierarchy(repository);
        repository.WorkItems.Add(MakeWorkItem(workstreamA.WorkstreamKey, estimatedHours: 5m));
        repository.WorkItems.Add(MakeWorkItem(workstreamB.WorkstreamKey, estimatedHours: 7m));

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, TestTenantId, programmeFilter: programmeA.ProgrammeKey);

        var row = Assert.Single(model.EffortVariance);
        Assert.Equal(workstreamA.WorkstreamKey, row.WorkstreamKey);
    }

    [Fact]
    public async Task BuildHubAsync_scopes_effort_variance_by_customer_filter()
    {
        var repository = new FakeProgrammeRepository();
        var customer = new Customer { CustomerKey = Guid.NewGuid(), Name = "Acme", TenantId = TestTenantId, CreatedAtUtc = Now.UtcDateTime, UpdatedAtUtc = Now.UtcDateTime };
        repository.Customers.Add(customer);

        var (_, _, workstreamWithCustomer) = SeedHierarchy(repository, customerKey: customer.CustomerKey);
        var (_, _, workstreamNoCustomer) = SeedHierarchy(repository);
        repository.WorkItems.Add(MakeWorkItem(workstreamWithCustomer.WorkstreamKey, estimatedHours: 5m));
        repository.WorkItems.Add(MakeWorkItem(workstreamNoCustomer.WorkstreamKey, estimatedHours: 7m));

        var sut = BuildSut(repository, new FakeStaffRepository(), new FakeAvailabilityRepository(), new FakeStaffRateRepository());
        var model = await sut.BuildHubAsync(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), null, TestTenantId, customerFilter: customer.CustomerKey);

        var row = Assert.Single(model.EffortVariance);
        Assert.Equal(workstreamWithCustomer.WorkstreamKey, row.WorkstreamKey);
    }

    [Fact]
    public async Task LockBaselineAsync_locks_once_and_ignores_a_later_relock_attempt()
    {
        var repository = new FakeProgrammeRepository();
        var workstreamKey = Guid.NewGuid();

        await repository.LockBaselineAsync(workstreamKey, 10m, null, Now.UtcDateTime, TestTenantId);
        var second = await repository.LockBaselineAsync(workstreamKey, 999m, null, Now.UtcDateTime.AddDays(1), TestTenantId);

        Assert.Equal(10m, second.BaselineHours);
        Assert.Single(repository.WorkstreamBaselines);
    }
}
