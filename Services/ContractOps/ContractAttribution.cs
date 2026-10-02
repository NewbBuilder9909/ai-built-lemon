using ProgrammePulse.Models.ContractOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Decides whether a contract's cost figure belongs to that contract alone.
///
/// ContractCommercialService selects cost by walking Customer -&gt; Programme
/// -&gt; Project -&gt; Workstream -&gt; WorkItem -&gt; TimeEntry and then filtering
/// on the contract's own term dates. There is no explicit
/// contract-to-work-item allocation anywhere in the schema, so when one
/// customer holds two contracts whose terms overlap in time, the same hours
/// are selected by both and counted twice across the portfolio. This is the
/// single largest obstacle to presenting a finance-grade margin number, so
/// it is detected and named rather than silently tolerated.
///
/// Pure and static on purpose — same shape as TimeEntryCostCalculator, and
/// directly unit-testable without a repository.
/// </summary>
public static class ContractAttribution
{
    /// <summary>
    /// Two contract terms overlap when each starts on or before the other
    /// ends. Inclusive at both ends, matching
    /// ContractCommercialService's own inclusive term filter — a contract
    /// ending on the day another begins really does select that day's hours
    /// twice.
    /// </summary>
    public static bool TermsOverlap(Contract left, Contract right) =>
        left.StartDate <= right.EndDate && right.StartDate <= left.EndDate;

    /// <summary>
    /// The other contracts, from the same tenant, that select the same work
    /// as <paramref name="contract"/>: same customer, overlapping term.
    /// Ordered by reference so the reviewer sees a stable list.
    /// </summary>
    public static IReadOnlyList<Contract> FindOverlapping(Contract contract, IEnumerable<Contract> allContracts) =>
        allContracts
            .Where(other => other.ContractKey != contract.ContractKey)
            .Where(other => other.CustomerKey == contract.CustomerKey)
            .Where(other => TermsOverlap(contract, other))
            .OrderBy(other => other.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// True when a date falls inside a contract's term, using the same
    /// inclusive comparison ContractCommercialService applies to time
    /// entries.
    /// </summary>
    public static bool ContainsDate(Contract contract, DateOnly date) =>
        date >= contract.StartDate && date <= contract.EndDate;
}
