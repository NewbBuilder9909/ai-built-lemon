using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Tests.ProgrammeOps;

namespace ProgrammePulse.Tests.ContractOps;

public class ContractCommercialServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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

    private static readonly DateTime Now = new(2031, 1, 1);

    private static ContractCommercialService BuildSut(FakeContractRepository contracts, FakeProgrammeRepository programmes, FakeStaffRateRepository rates) =>
        new(contracts, programmes, rates);

    private static (Customer Customer, Guid WorkItemKey) SeedCustomerWithWorkItem(FakeProgrammeRepository repository)
    {
        var customer = new Customer { CustomerKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Acme Broadcasting", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        repository.Customers.Add(customer);

        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Drama", CustomerKey = customer.CustomerKey, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var project = new Project { ProjectKey = Guid.NewGuid(), TenantId = TestTenantId, ProgrammeKey = programme.ProgrammeKey, Name = "Series 4", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workstream = new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = TestTenantId, ProjectKey = project.ProjectKey, Name = "Post", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workItem = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "Edit", Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = Now, UpdatedAtUtc = Now };

        repository.Programmes.Add(programme);
        repository.Projects.Add(project);
        repository.Workstreams.Add(workstream);
        repository.WorkItems.Add(workItem);

        return (customer, workItem.WorkItemKey);
    }

    private static TimeEntry MakeEntry(Guid workItemKey, Guid staffKey, decimal hours, DateTime startedAtUtc, bool billable = true) => new()
    {
        TimeEntryKey = Guid.NewGuid(),
        TenantId = TestTenantId,
        WorkItemKey = workItemKey,
        StaffKey = staffKey,
        DurationHours = hours,
        StartedAtUtc = startedAtUtc,
        IsBillable = billable,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    [Fact]
    public async Task Ongoing_contract_produces_a_yearly_burn_down_with_correct_cumulative_totals()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 50m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        // Year 1 (2026-01-01 to 2027-01-01): 100 hours -> £5,000 cost against £350,000 value.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 100m, new DateTime(2026, 3, 1)));
        // Year 2: 200 hours -> £10,000 cost.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 200m, new DateTime(2027, 3, 1)));

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "MSA-2026-01",
            CommercialModel = CommercialModel.Ongoing,
            AnnualValue = 350_000m,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2031, 1, 1),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Equal(5, position.YearlyBreakdown.Count);
        Assert.Equal(1_750_000m, position.ContractCeiling);

        var year1 = position.YearlyBreakdown[0];
        Assert.Equal(350_000m, year1.Value);
        Assert.Equal(5_000m, year1.Cost);
        Assert.Equal(345_000m, year1.Margin);
        Assert.Equal(350_000m, year1.CumulativeValue);

        var year2 = position.YearlyBreakdown[1];
        Assert.Equal(10_000m, year2.Cost);
        Assert.Equal(700_000m, year2.CumulativeValue);
        Assert.Equal(15_000m, year2.CumulativeCost);

        // Only 2 of 5 years have activity — cumulative totals stop accruing cost after year 2,
        // but the contract's cumulative value keeps recognising all 5 years (£1,750,000 ceiling).
        Assert.Equal(1_750_000m, position.CumulativeRevenue);
        Assert.Equal(15_000m, position.CumulativeCost);
        Assert.Equal(1_735_000m, position.Margin);
    }

    [Fact]
    public async Task FixedPrice_contract_reports_one_overall_figure_with_no_yearly_rows()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 50m, new DateTime(2026, 6, 1)));

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "Fixed scope build",
            CommercialModel = CommercialModel.FixedPrice,
            TotalContractValue = 10_000m,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Empty(position.YearlyBreakdown);
        Assert.Equal(10_000m, position.ContractCeiling);
        Assert.Equal(2_000m, position.CumulativeCost);
        Assert.Equal(8_000m, position.Margin);
        Assert.Equal(80m, position.MarginPercent);
    }

    [Fact]
    public async Task TimeAndMaterials_with_a_bill_rate_computes_revenue_from_billable_hours()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 10m, new DateTime(2026, 6, 1), billable: true));
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 4m, new DateTime(2026, 6, 2), billable: false));

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "T&M engagement",
            CommercialModel = CommercialModel.TimeAndMaterials,
            BillRate = 100m,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.False(position.RevenueUnset);
        Assert.Equal(1_000m, position.CumulativeRevenue); // 10 billable hrs x £100
        Assert.Equal(560m, position.CumulativeCost); // 14 hrs x £40
        Assert.Equal(440m, position.Margin);
    }

    [Fact]
    public async Task TimeAndMaterials_with_no_bill_rate_reports_revenue_as_unset_not_zero()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 10m, new DateTime(2026, 6, 1)));

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "T&M engagement, no rate yet",
            CommercialModel = CommercialModel.TimeAndMaterials,
            BillRate = null,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Draft,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.True(position.RevenueUnset);
        Assert.Null(position.CumulativeRevenue);
        Assert.Null(position.Margin);
        Assert.Equal(400m, position.CumulativeCost); // cost is still tracked
    }

    [Fact]
    public async Task A_cost_entry_in_a_different_currency_is_excluded_and_surfaced_separately()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "USD", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 10m, new DateTime(2026, 6, 1)));

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "GBP contract, USD-rated staff",
            CommercialModel = CommercialModel.FixedPrice,
            TotalContractValue = 10_000m,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Equal(0m, position.CumulativeCost);
        Assert.Equal(10m, position.MismatchedCurrencyHours);
    }

    [Fact]
    public async Task Non_labour_costs_in_the_contract_currency_are_folded_into_cumulative_cost()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, _) = SeedCustomerWithWorkItem(programmes);

        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = "Fixed price with expenses",
            CommercialModel = CommercialModel.FixedPrice,
            TotalContractValue = 10_000m,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        contracts.Contracts.Add(contract);
        contracts.NonLabourCosts.Add(new NonLabourCost
        {
            NonLabourCostKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            ContractKey = contract.ContractKey,
            Description = "Studio hire",
            Amount = 500m,
            Currency = "GBP",
            IncurredOn = new DateOnly(2026, 3, 1),
            CreatedAtUtc = Now
        });
        contracts.NonLabourCosts.Add(new NonLabourCost
        {
            NonLabourCostKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            ContractKey = contract.ContractKey,
            Description = "US vendor invoice",
            Amount = 200m,
            Currency = "USD",
            IncurredOn = new DateOnly(2026, 3, 2),
            CreatedAtUtc = Now
        });

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Equal(500m, position.CumulativeCost); // no labour cost this test, just the GBP expense
        Assert.Equal(500m, position.NonLabourCostTotal);
        Assert.Equal(200m, position.MismatchedCurrencyNonLabourCost); // USD entry excluded, surfaced separately
        Assert.Equal(9_500m, position.Margin);
        Assert.Equal(2, position.NonLabourCosts.Count);
    }

    // ---- Cost attribution (the finance-demo gate) --------------------------
    //
    // Cost is *selected* by customer + contract term, never *allocated* to a
    // contract, so two overlapping contracts for one customer both pick up the
    // same hours. These tests pin the behaviour that the double-count is named
    // and the derived profit withheld, rather than presented as margin.

    private static Contract FixedPriceContract(Guid customerKey, string reference, decimal value, DateOnly start, DateOnly end) => new()
    {
        ContractKey = Guid.NewGuid(),
        TenantId = TestTenantId,
        CustomerKey = customerKey,
        Reference = reference,
        CommercialModel = CommercialModel.FixedPrice,
        TotalContractValue = value,
        Currency = "GBP",
        StartDate = start,
        EndDate = end,
        Status = ContractStatus.Active,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    [Fact]
    public async Task Two_overlapping_contracts_for_one_customer_suppress_margin_and_report_the_shared_cost()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        // 50 hours in June, inside both contracts' terms -> £2,000 selected twice.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 50m, new DateTime(2026, 6, 1)));

        var first = FixedPriceContract(customer.CustomerKey, "Build phase 1", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var second = FixedPriceContract(customer.CustomerKey, "Support retainer", 6_000m, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        contracts.Contracts.Add(first);
        contracts.Contracts.Add(second);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(first.ContractKey, TestTenantId);

        Assert.True(position.Attribution.Contested);
        Assert.Equal(["Support retainer"], position.Attribution.OverlappingContractReferences);
        Assert.Equal(2_000m, position.Attribution.SharedCost);

        // Cost stays — the hours were really incurred for this customer.
        Assert.Equal(2_000m, position.CumulativeCost);
        // Margin does not — it would read as this contract's own profit.
        Assert.Null(position.Margin);
        Assert.Null(position.MarginPercent);
        Assert.Equal(MarginBasis.NotEstablished, position.MarginBasis);
    }

    [Fact]
    public async Task Contracts_for_the_same_customer_with_non_overlapping_terms_keep_their_margin()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 50m, new DateTime(2026, 3, 1)));

        var first = FixedPriceContract(customer.CustomerKey, "Build phase 1", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var second = FixedPriceContract(customer.CustomerKey, "Build phase 2", 6_000m, new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31));
        contracts.Contracts.Add(first);
        contracts.Contracts.Add(second);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(first.ContractKey, TestTenantId);

        Assert.False(position.Attribution.Contested);
        Assert.Equal(2_000m, position.CumulativeCost);
        Assert.Equal(8_000m, position.Margin);
        Assert.Equal(MarginBasis.CostToDateHeadroom, position.MarginBasis);
    }

    [Fact]
    public async Task A_contract_for_a_different_customer_never_contests_attribution()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 50m, new DateTime(2026, 6, 1)));

        var mine = FixedPriceContract(customer.CustomerKey, "Build phase 1", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var someoneElses = FixedPriceContract(Guid.NewGuid(), "Other customer", 5_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        contracts.Contracts.Add(mine);
        contracts.Contracts.Add(someoneElses);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(mine.ContractKey, TestTenantId);

        Assert.False(position.Attribution.Contested);
        Assert.Equal(8_000m, position.Margin);
    }

    [Fact]
    public async Task An_undated_time_entry_is_excluded_from_cost_and_reported_separately()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 50m, new DateTime(2026, 6, 1)));
        programmes.TimeEntries.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            WorkItemKey = workItemKey,
            StaffKey = staffKey,
            DurationHours = 9m,
            StartedAtUtc = null,
            WorkDate = null,
            IsBillable = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        });

        var contract = FixedPriceContract(customer.CustomerKey, "Build", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        // The undated 9 hours can be placed in no term, so they are neither
        // costed here nor allowed to drift into every contract for the customer.
        Assert.Equal(2_000m, position.CumulativeCost);
        Assert.Equal(9m, position.UndatedHours);
    }

    [Fact]
    public async Task An_entry_dated_only_by_WorkDate_is_term_filtered_on_that_date()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        // WorkDate only, and well outside the contract term — previously this
        // bypassed the term filter entirely and was costed anyway.
        programmes.TimeEntries.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            WorkItemKey = workItemKey,
            StaffKey = staffKey,
            DurationHours = 25m,
            StartedAtUtc = null,
            WorkDate = new DateOnly(2028, 5, 5),
            IsBillable = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        });

        var contract = FixedPriceContract(customer.CustomerKey, "Build", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        contracts.Contracts.Add(contract);

        var sut = BuildSut(contracts, programmes, rates);
        var position = await sut.BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Equal(0m, position.CumulativeCost);
        Assert.Equal(0m, position.UndatedHours); // dated, just not in this term
        Assert.Equal(10_000m, position.Margin);
    }

    [Fact]
    public async Task The_work_date_decides_the_term_when_it_and_the_UTC_start_fall_on_different_days()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 40m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2025, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2025, 1, 1) });

        // Logged on 1 January by the provider's calendar, stamped 31 December in
        // UTC. Reports and invoices put it in January (decision 4), so costing
        // must too, or the margin and the invoice disagree about the same hours.
        programmes.TimeEntries.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            WorkItemKey = workItemKey,
            StaffKey = staffKey,
            DurationHours = 10m,
            StartedAtUtc = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc),
            WorkDate = new DateOnly(2026, 1, 1),
            IsBillable = true,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        });

        var contract = FixedPriceContract(customer.CustomerKey, "Build", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        contracts.Contracts.Add(contract);

        var position = await BuildSut(contracts, programmes, rates).BuildCommercialPositionAsync(contract.ContractKey, TestTenantId);

        Assert.Equal(400m, position.CumulativeCost);
    }

    [Fact]
    public async Task BuildSummaryAsync_flags_the_contested_rows_so_the_portfolio_rollup_can_hold_them_out()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, _) = SeedCustomerWithWorkItem(programmes);
        contracts.Contracts.Add(FixedPriceContract(customer.CustomerKey, "Overlap A", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        contracts.Contracts.Add(FixedPriceContract(customer.CustomerKey, "Overlap B", 5_000m, new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31)));
        contracts.Contracts.Add(FixedPriceContract(Guid.NewGuid(), "Clean", 3_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        var sut = BuildSut(contracts, programmes, rates);
        var summary = await sut.BuildSummaryAsync(TestTenantId);

        Assert.Equal(2, summary.Rows.Count(r => r.AttributionContested));
        Assert.Single(summary.Rows, r => !r.AttributionContested && r.Reference == "Clean");
    }

    [Fact]
    public async Task BuildSummaryAsync_counts_shared_hours_once_in_the_held_out_cost()
    {
        // Two overlapping contracts for one customer. 100 hours fall inside
        // both terms, so both report them as their own cost — the held-out
        // total must be the cost of the hours, not the sum of the two
        // contracts' cost figures, or the number that measures the
        // double-count would itself double-count.
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, workItemKey) = SeedCustomerWithWorkItem(programmes);
        var staffKey = Guid.NewGuid();
        rates.Rates.Add(new StaffRate { StaffKey = staffKey, CostPerHour = 50m, RateCurrency = "GBP", EffectiveFromUtc = new DateTime(2026, 1, 1), ChangedByStaffKey = staffKey, ChangedAtUtc = new DateTime(2026, 1, 1) });

        // Inside both terms: 100 hours -> £5,000, claimed by A and by B.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 100m, new DateTime(2026, 8, 1)));
        // Inside A only: 20 hours -> £1,000.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 20m, new DateTime(2026, 2, 1)));
        // Inside B only: 40 hours -> £2,000.
        programmes.TimeEntries.Add(MakeEntry(workItemKey, staffKey, 40m, new DateTime(2027, 2, 1)));

        contracts.Contracts.Add(FixedPriceContract(customer.CustomerKey, "Overlap A", 10_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        contracts.Contracts.Add(FixedPriceContract(customer.CustomerKey, "Overlap B", 5_000m, new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31)));

        var sut = BuildSut(contracts, programmes, rates);
        var summary = await sut.BuildSummaryAsync(TestTenantId);

        // Each contract still reports its own selection: A = 6,000, B = 7,000.
        Assert.Equal(6_000m, summary.Rows.Single(r => r.Reference == "Overlap A").CumulativeCost);
        Assert.Equal(7_000m, summary.Rows.Single(r => r.Reference == "Overlap B").CumulativeCost);

        var held = Assert.Single(summary.ContestedCost);
        Assert.Equal("GBP", held.Currency);
        Assert.Equal(8_000m, held.TotalCost);               // 1,000 + 5,000 + 2,000, not 13,000
        Assert.Equal(5_000m, held.DisputedCost);            // the shared hours, named
        Assert.Equal(2, held.ContractCount);
    }

    [Fact]
    public async Task BuildSummaryAsync_reports_no_held_out_cost_when_nothing_is_contested()
    {
        var programmes = new FakeProgrammeRepository();
        var contracts = new FakeContractRepository();
        var rates = new FakeStaffRateRepository();

        var (customer, _) = SeedCustomerWithWorkItem(programmes);
        contracts.Contracts.Add(FixedPriceContract(customer.CustomerKey, "Clean", 3_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        var sut = BuildSut(contracts, programmes, rates);
        var summary = await sut.BuildSummaryAsync(TestTenantId);

        Assert.Empty(summary.ContestedCost);
    }
}
