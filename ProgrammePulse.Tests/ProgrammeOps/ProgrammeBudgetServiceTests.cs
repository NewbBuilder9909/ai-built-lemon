using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class ProgrammeBudgetServiceTests
{
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

    private static readonly DateTime Now = new(2026, 9, 1);
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (Programme Programme, Guid WorkItemKey) SeedProgrammeWithWorkItem(FakeProgrammeRepository repository, Guid? customerKey = null)
    {
        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Drama", CustomerKey = customerKey, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var project = new Project { ProjectKey = Guid.NewGuid(), TenantId = TestTenantId, ProgrammeKey = programme.ProgrammeKey, Name = "Series 4", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workstream = new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = TestTenantId, ProjectKey = project.ProjectKey, Name = "Post", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workItem = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "Edit", Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = Now, UpdatedAtUtc = Now };

        repository.Programmes.Add(programme);
        repository.Projects.Add(project);
        repository.Workstreams.Add(workstream);
        repository.WorkItems.Add(workItem);

        return (programme, workItem.WorkItemKey);
    }

    [Fact]
    public async Task BuildBudgetSummaryAsync_computes_burn_percent_against_the_set_budget()
    {
        var repository = new FakeProgrammeRepository();
        var rates = new FakeStaffRateRepository();
        var (programme, workItemKey) = SeedProgrammeWithWorkItem(repository);
        repository.Programmes.Remove(programme);
        programme = programme with { BudgetAmount = 10_000m, BudgetCurrency = "GBP" };
        repository.Programmes.Add(programme);

        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 50m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        repository.TimeEntries.Add(new TimeEntry { TimeEntryKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = workItemKey, StaffKey = staffKey, DurationHours = 40m, StartedAtUtc = new DateTime(2026, 3, 1), IsBillable = true, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        var sut = new ProgrammeBudgetService(repository, rates);
        var rows = await sut.BuildBudgetSummaryAsync(TestTenantId);

        var row = Assert.Single(rows, r => r.ProgrammeKey == programme.ProgrammeKey);
        Assert.Equal(2_000m, row.CostToDate); // 40 hrs x £50
        Assert.Equal(20m, row.BurnPercent); // 2000 / 10000 * 100
    }

    [Fact]
    public async Task BuildBudgetSummaryAsync_leaves_burn_percent_null_when_no_budget_is_set()
    {
        var repository = new FakeProgrammeRepository();
        var rates = new FakeStaffRateRepository();
        var (programme, _) = SeedProgrammeWithWorkItem(repository);

        var sut = new ProgrammeBudgetService(repository, rates);
        var rows = await sut.BuildBudgetSummaryAsync(TestTenantId);

        var row = Assert.Single(rows, r => r.ProgrammeKey == programme.ProgrammeKey);
        Assert.Null(row.BurnPercent);
        Assert.Null(row.BudgetAmount);
    }

    [Fact]
    public async Task BuildBudgetSummaryAsync_excludes_a_currency_mismatched_entry_from_cost_to_date()
    {
        var repository = new FakeProgrammeRepository();
        var rates = new FakeStaffRateRepository();
        var (programme, workItemKey) = SeedProgrammeWithWorkItem(repository);
        repository.Programmes.Remove(programme);
        programme = programme with { BudgetAmount = 1_000m, BudgetCurrency = "GBP" };
        repository.Programmes.Add(programme);

        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "USD", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        repository.TimeEntries.Add(new TimeEntry { TimeEntryKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = workItemKey, StaffKey = staffKey, DurationHours = 10m, StartedAtUtc = new DateTime(2026, 3, 1), IsBillable = true, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        var sut = new ProgrammeBudgetService(repository, rates);
        var rows = await sut.BuildBudgetSummaryAsync(TestTenantId);

        var row = Assert.Single(rows, r => r.ProgrammeKey == programme.ProgrammeKey);
        Assert.Equal(0m, row.CostToDate);
        Assert.Equal(10m, row.MismatchedCurrencyHours);
    }
}
