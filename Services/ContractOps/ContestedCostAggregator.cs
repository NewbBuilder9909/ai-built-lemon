using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Totals the cost sitting in contracts whose attribution is contested,
/// counting each underlying hour once.
///
/// This exists because the obvious implementation is wrong. Two overlapping
/// contracts for one customer each select the same time entries, so each
/// reports that cost as its own — adding their CumulativeCost figures counts
/// those hours twice and inflates the very number that is supposed to
/// measure how much of the book is unreportable. Deduplication has to happen
/// where the time entries are still in hand, so it happens here, keyed on
/// <see cref="TimeEntry.TimeEntryKey"/>, and is driven from
/// ContractCommercialService's single walk of the book.
///
/// Non-labour costs are summed plainly: a <see cref="Models.ContractOps.NonLabourCost"/>
/// row is attached to one contract explicitly, so it is allocated, not
/// selected, and two contracts can never share one.
///
/// Pure and static on purpose — same shape as <see cref="ContractAttribution"/>
/// and TimeEntryCostCalculator, and directly unit-testable without a
/// repository.
/// </summary>
public static class ContestedCostAggregator
{
    /// <param name="InTermEntries">Every time entry this contract selects — the same list it costed itself from.</param>
    /// <param name="RateHistory">Rate history for the staff on those entries, so the dedup can re-cost the union identically.</param>
    /// <param name="NonLabourCost">This contract's own matching-currency non-labour cost.</param>
    public readonly record struct ContestedContractCost(
        string Currency,
        IReadOnlyList<TimeEntry> InTermEntries,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> RateHistory,
        decimal NonLabourCost);

    /// <summary>
    /// One row per currency — never a blended total across them, the same
    /// rule every other figure in Contract Ops follows.
    /// </summary>
    public static IReadOnlyList<ContestedCostRowViewModel> Aggregate(IEnumerable<ContestedContractCost> contracts) =>
        contracts
            .GroupBy(c => c.Currency, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildRow(group.Key, group.ToList()))
            .OrderBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ContestedCostRowViewModel BuildRow(string currency, List<ContestedContractCost> contracts)
    {
        var distinctEntries = new Dictionary<Guid, TimeEntry>();
        var claimsPerEntry = new Dictionary<Guid, int>();
        var rateHistory = new Dictionary<Guid, IReadOnlyList<StaffRate>>();
        var naiveLabourCost = 0m;
        var nonLabourCost = 0m;

        foreach (var contract in contracts)
        {
            foreach (var (staffKey, history) in contract.RateHistory)
            {
                // Same staff member, same history, whichever contract supplied it.
                rateHistory[staffKey] = history;
            }

            // Counted per contract, not per row: one contract claiming the
            // same entry twice would otherwise read as two contracts
            // disputing it. A repository query can't return an entry twice,
            // but the count only means "how many contracts" if it is taken
            // that way.
            foreach (var entryKey in contract.InTermEntries.Select(e => e.TimeEntryKey).Distinct())
            {
                claimsPerEntry[entryKey] = claimsPerEntry.GetValueOrDefault(entryKey) + 1;
            }

            foreach (var entry in contract.InTermEntries)
            {
                distinctEntries[entry.TimeEntryKey] = entry;
            }

            // Recomputed through the same calculator rather than taken from
            // the caller, so the overstatement figure can't drift from the
            // deduplicated one it is measured against.
            naiveLabourCost += TimeEntryCostCalculator.Calculate(contract.InTermEntries, contract.RateHistory, currency).Cost;
            nonLabourCost += contract.NonLabourCost;
        }

        var distinctLabourCost = TimeEntryCostCalculator
            .Calculate([.. distinctEntries.Values], rateHistory, currency)
            .Cost;

        // The hours more than one held-out contract claims, costed once. This
        // is the amount actually under dispute, and it is a different
        // quantity from the overstatement below: an entry claimed by three
        // contracts is disputed once but inflates a naive sum twice.
        var disputedLabourCost = TimeEntryCostCalculator
            .Calculate([.. distinctEntries.Where(e => claimsPerEntry[e.Key] > 1).Select(e => e.Value)], rateHistory, currency)
            .Cost;

        return new ContestedCostRowViewModel(
            currency,
            distinctLabourCost + nonLabourCost,
            disputedLabourCost,
            naiveLabourCost - distinctLabourCost,
            contracts.Count);
    }
}
