using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ContractOps;

namespace ProgrammePulse.Tests.ContractOps;

/// <summary>
/// The arithmetic behind "cost held out of the portfolio totals". Pure, so
/// it is exercised directly rather than through ContractCommercialService —
/// same treatment as ContractAttribution.
/// </summary>
public class ContestedCostAggregatorTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2031, 1, 1);

    private static TimeEntry Entry(Guid staffKey, decimal hours, DateTime startedAtUtc) => new()
    {
        TimeEntryKey = Guid.NewGuid(),
        TenantId = TestTenantId,
        WorkItemKey = Guid.NewGuid(),
        StaffKey = staffKey,
        DurationHours = hours,
        StartedAtUtc = startedAtUtc,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static Dictionary<Guid, IReadOnlyList<StaffRate>> RateHistory(Guid staffKey, decimal costPerHour, string currency = "GBP") =>
        new()
        {
            [staffKey] =
            [
                new StaffRate
                {
                    StaffKey = staffKey,
                    CostPerHour = costPerHour,
                    RateCurrency = currency,
                    EffectiveFromUtc = new DateTime(2020, 1, 1),
                    ChangedByStaffKey = staffKey,
                    ChangedAtUtc = new DateTime(2020, 1, 1)
                }
            ]
        };

    [Fact]
    public void Aggregate_counts_a_shared_time_entry_once_across_two_contracts()
    {
        // The whole point. Both contracts select the same 100-hour entry, so
        // both report £5,000 of cost. Held out: £5,000, not £10,000.
        var staffKey = Guid.NewGuid();
        var rates = RateHistory(staffKey, 50m);
        var shared = Entry(staffKey, 100m, new DateTime(2026, 8, 1));

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 0m)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(5_000m, row.TotalCost);
        Assert.Equal(5_000m, row.DisputedCost);
        Assert.Equal(5_000m, row.DuplicationIfSummed);
        Assert.Equal(2, row.ContractCount);
    }

    [Fact]
    public void Aggregate_keeps_the_part_only_one_contract_selects()
    {
        // Overlap is partial in practice: each contract also has hours the
        // other one's term doesn't reach. Those are counted normally.
        var staffKey = Guid.NewGuid();
        var rates = RateHistory(staffKey, 50m);
        var shared = Entry(staffKey, 100m, new DateTime(2026, 8, 1));   // £5,000
        var onlyA = Entry(staffKey, 20m, new DateTime(2026, 2, 1));     // £1,000
        var onlyB = Entry(staffKey, 40m, new DateTime(2027, 2, 1));     // £2,000

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost("GBP", [onlyA, shared], rates, NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared, onlyB], rates, NonLabourCost: 0m)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(8_000m, row.TotalCost);
        Assert.Equal(5_000m, row.DisputedCost);
        Assert.Equal(5_000m, row.DuplicationIfSummed);
    }

    [Fact]
    public void Aggregate_sums_non_labour_cost_plainly_because_it_is_allocated_not_selected()
    {
        // A NonLabourCost row is attached to one contract by key, so two
        // contracts can never share one — no deduplication applies, and
        // halving it would understate the held-out figure.
        var staffKey = Guid.NewGuid();
        var rates = RateHistory(staffKey, 50m);
        var shared = Entry(staffKey, 100m, new DateTime(2026, 8, 1));

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 300m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 700m)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(6_000m, row.TotalCost);                // 5,000 shared labour + 300 + 700
        Assert.Equal(5_000m, row.DisputedCost);
        Assert.Equal(5_000m, row.DuplicationIfSummed); // labour only — the non-labour isn't contested
    }

    [Fact]
    public void Aggregate_separates_disputed_cost_from_the_overstatement_a_naive_sum_would_produce()
    {
        // Three contracts claiming the same £100 of hours. Those two figures
        // are different quantities and must not be conflated: £100 is under
        // dispute, but summing the three contracts' own cost figures would
        // have reported £300 — an overstatement of £200. A page that labelled
        // the £200 as "disputed cost" would be overstating the exposure by
        // exactly the amount the deduplication was meant to remove.
        var staffKey = Guid.NewGuid();
        var rates = RateHistory(staffKey, 50m);
        var shared = Entry(staffKey, 2m, new DateTime(2026, 8, 1)); // £100

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [shared], rates, NonLabourCost: 0m)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(100m, row.TotalCost);
        Assert.Equal(100m, row.DisputedCost);          // claimed by more than one, counted once
        Assert.Equal(200m, row.DuplicationIfSummed);   // 300 naive - 100 distinct
        Assert.Equal(3, row.ContractCount);
    }

    [Fact]
    public void Aggregate_reports_no_disputed_cost_when_held_out_contracts_share_no_hours()
    {
        // A contract can be contested — its term overlaps another's — while
        // the hours it actually selects fall outside the overlap. Nothing is
        // then in dispute, and claiming otherwise would invent an exposure.
        var staffKey = Guid.NewGuid();
        var rates = RateHistory(staffKey, 50m);

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost("GBP", [Entry(staffKey, 10m, new DateTime(2026, 2, 1))], rates, NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost("GBP", [Entry(staffKey, 20m, new DateTime(2026, 9, 1))], rates, NonLabourCost: 0m)
        ]);

        var row = Assert.Single(rows);
        Assert.Equal(1_500m, row.TotalCost);
        Assert.Equal(0m, row.DisputedCost);
        Assert.Equal(0m, row.DuplicationIfSummed);
    }

    [Fact]
    public void Aggregate_never_blends_across_currencies()
    {
        var gbpStaff = Guid.NewGuid();
        var usdStaff = Guid.NewGuid();

        var rows = ContestedCostAggregator.Aggregate(
        [
            new ContestedCostAggregator.ContestedContractCost(
                "GBP", [Entry(gbpStaff, 10m, new DateTime(2026, 8, 1))], RateHistory(gbpStaff, 50m), NonLabourCost: 0m),
            new ContestedCostAggregator.ContestedContractCost(
                "USD", [Entry(usdStaff, 10m, new DateTime(2026, 8, 1))], RateHistory(usdStaff, 80m, "USD"), NonLabourCost: 0m)
        ]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(500m, Assert.Single(rows, r => r.Currency == "GBP").TotalCost);
        Assert.Equal(800m, Assert.Single(rows, r => r.Currency == "USD").TotalCost);
    }

    [Fact]
    public void Aggregate_returns_nothing_when_no_contract_is_contested()
    {
        Assert.Empty(ContestedCostAggregator.Aggregate([]));
    }
}
