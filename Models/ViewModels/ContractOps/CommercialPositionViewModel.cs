using ProgrammePulse.Models.ContractOps;

namespace ProgrammePulse.Models.ViewModels.ContractOps;

/// <summary>
/// Admin-only, same as Cost Summary — nothing in this namespace should ever
/// be rendered on a page a non-Admin can reach.
/// </summary>
public sealed record CommercialPositionViewModel(
    Guid ContractKey,
    string Reference,
    string CustomerName,
    CommercialModel CommercialModel,
    ContractStatus Status,
    string Currency,
    decimal? ContractCeiling,
    decimal CumulativeCost,
    decimal? CumulativeRevenue,
    decimal? Margin,
    decimal? MarginPercent,
    bool RevenueUnset,
    decimal UnpricedHours,
    decimal MismatchedCurrencyHours,
    IReadOnlyList<ContractYearRowViewModel> YearlyBreakdown,
    decimal NonLabourCostTotal,
    decimal MismatchedCurrencyNonLabourCost,
    IReadOnlyList<NonLabourCostRowViewModel> NonLabourCosts,
    ContractAttributionViewModel Attribution,
    MarginBasis MarginBasis,
    decimal UndatedHours);

/// <summary>
/// Why a contract's cost figure can or can't be treated as its own.
/// Cost is selected through Customer -&gt; Programme -&gt; ... -&gt; TimeEntry
/// plus the contract's own term dates — there is no explicit
/// contract-to-work allocation, so two contracts for the same customer whose
/// terms overlap in time both select the same hours. That is a real
/// double-count, not a rounding concern, so it is named here and the derived
/// margin is suppressed rather than presented as independently attributed
/// profit (see docs/contract-ops.md, "Cost attribution").
/// </summary>
/// <param name="Contested">True when at least one other contract for the same customer overlaps this term.</param>
/// <param name="OverlappingContractReferences">The references of those contracts, so the reviewer can go and look.</param>
/// <param name="SharedCost">Cost selected by this contract that at least one overlapping contract also selects.</param>
public sealed record ContractAttributionViewModel(
    bool Contested,
    IReadOnlyList<string> OverlappingContractReferences,
    decimal SharedCost)
{
    public static readonly ContractAttributionViewModel Uncontested = new(false, [], 0m);
}

/// <summary>
/// What the Margin figure on a commercial position actually measures. The
/// three commercial models produce three different things and only one of
/// them is close to a realised margin, so the basis travels with the number
/// instead of being implied by a column header.
/// </summary>
public enum MarginBasis
{
    /// <summary>Margin is suppressed — cost attribution is contested or revenue is unset.</summary>
    NotEstablished = 0,

    /// <summary>
    /// Fixed price: contract value less cost incurred to date. Headroom
    /// remaining, not profit — the cost of completing the remaining scope is
    /// not included and is not known here.
    /// </summary>
    CostToDateHeadroom = 1,

    /// <summary>
    /// Ongoing: cumulative contracted annual value less cumulative cost to
    /// date. Contracted value, not recognised revenue and not cash.
    /// </summary>
    ContractedValueLessCostToDate = 2,

    /// <summary>
    /// Time &amp; materials: billable hours × bill rate, less cost to date.
    /// Chargeable value, not invoiced and not cash.
    /// </summary>
    ChargeableValueLessCostToDate = 3
}

public static class MarginBasisText
{
    public static string Describe(MarginBasis basis) => basis switch
    {
        MarginBasis.CostToDateHeadroom => "Contract value less cost to date — headroom remaining, not final margin. The cost of completing the remaining scope is not included.",
        MarginBasis.ContractedValueLessCostToDate => "Cumulative contracted value less cost to date. Contracted value is not recognised revenue and not cash.",
        MarginBasis.ChargeableValueLessCostToDate => "Billable hours × bill rate, less cost to date. Chargeable value is not invoiced and not cash.",
        _ => "Not established — this contract's cost cannot be attributed to it alone, or its revenue is not set."
    };

    /// <summary>Short column-header suffix, for places with no room for the full sentence.</summary>
    public static string ShortLabel(MarginBasis basis) => basis switch
    {
        MarginBasis.CostToDateHeadroom => "headroom to date",
        MarginBasis.ContractedValueLessCostToDate => "contracted less cost",
        MarginBasis.ChargeableValueLessCostToDate => "chargeable less cost",
        _ => "not established"
    };
}

public sealed record NonLabourCostRowViewModel(
    Guid NonLabourCostKey,
    string Description,
    decimal Amount,
    string Currency,
    DateOnly IncurredOn);

/// <summary>One row per contract-year — only populated for the Ongoing commercial model.</summary>
public sealed record ContractYearRowViewModel(
    int YearNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Value,
    decimal Cost,
    decimal Margin,
    decimal CumulativeValue,
    decimal CumulativeCost,
    decimal CumulativeMargin);

/// <summary>
/// Everything the contracts list and the portfolio rollup need from a single
/// walk of the book: one row per contract, plus the cost sitting in the
/// contracts whose attribution is contested.
///
/// The contested figure belongs here rather than in the rollup because it can
/// only be computed correctly where the underlying time entries are still in
/// hand. Summing the contested rows' <see cref="ContractSummaryRowViewModel.CumulativeCost"/>
/// would count hours selected by two overlapping contracts twice — the exact
/// double-count the held-out figure exists to measure. See
/// ContestedCostAggregator.
/// </summary>
public sealed record ContractSummarySetViewModel(
    IReadOnlyList<ContractSummaryRowViewModel> Rows,
    IReadOnlyList<ContestedCostRowViewModel> ContestedCost);

/// <summary>One row of the high-level commercial position list (the Contracts index page).</summary>
public sealed record ContractSummaryRowViewModel(
    Guid ContractKey,
    string Reference,
    string CustomerName,
    CommercialModel CommercialModel,
    ContractStatus Status,
    string Currency,
    decimal? ContractCeiling,
    decimal CumulativeCost,
    decimal? Margin,
    decimal? MarginPercent,
    bool AttributionContested,
    MarginBasis MarginBasis);
