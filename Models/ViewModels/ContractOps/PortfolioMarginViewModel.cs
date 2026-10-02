using ProgrammePulse.Models.ContractOps;

namespace ProgrammePulse.Models.ViewModels.ContractOps;

/// <summary>
/// The cross-contract rollup shown on the Contracts Overview page.
/// Admin-only, same as CommercialPositionViewModel.
///
/// Every total is grouped by currency <em>and</em> by <see cref="MarginBasis"/>,
/// always. Currency because a blended cross-currency figure is meaningless;
/// basis because the three commercial models do not produce the same measure
/// — fixed-price "margin" is headroom against cost incurred so far with the
/// cost to complete unknown, Ongoing is contracted value less cost to date,
/// and only Time &amp; Materials is near a realised margin. Adding them gives a
/// number no one can act on, so this model deliberately exposes no single
/// portfolio margin at all: the basis travels with every figure, exactly as
/// it does on a single contract's position.
///
/// The figures are called Value rather than Revenue for the same reason.
/// None of the three is recognised revenue and none of it is cash.
///
/// A contract whose revenue is unset (Time &amp; Materials with no BillRate —
/// see ContractCommercialService.BuildTimeAndMaterialsPosition) is excluded
/// from every total and counted in MismatchedRevenueContractCount instead of
/// being assumed zero.
///
/// A contract whose cost attribution is contested (another contract for the
/// same customer overlaps its term, so both select the same hours — see
/// ContractAttribution) is held out on the same principle and counted in
/// AttributionContestedContractCount. Including it would double-count that
/// cost across the portfolio and overstate margin; assuming it away would
/// understate it. Neither is reportable, so it is named and excluded.
/// </summary>
/// <param name="AttributionContestedContractCount">
/// Contracts held out of every total because their cost is shared with an
/// overlapping contract for the same customer.
/// </param>
/// <param name="ContestedCost">
/// The cost sitting in those held-out contracts, per currency, so the size of
/// the unreported portion is visible rather than merely acknowledged.
/// Deduplicated by ContestedCostAggregator — summing the held-out contracts'
/// own cost figures would repeat the double-count this number exists to
/// measure.
/// </param>
public sealed record PortfolioMarginViewModel(
    IReadOnlyList<CurrencyTotalRowViewModel> TotalsByCurrency,
    IReadOnlyList<CustomerMarginRowViewModel> ByCustomer,
    IReadOnlyList<CommercialModelMarginRowViewModel> ByCommercialModel,
    int MismatchedRevenueContractCount,
    int AttributionContestedContractCount,
    IReadOnlyList<ContestedCostRowViewModel> ContestedCost);

/// <summary>
/// Cost held out of the portfolio totals because it is shared between
/// overlapping contracts, counted once per underlying time entry.
/// </summary>
/// <param name="TotalCost">
/// The distinct cost: hours selected by two held-out contracts are counted
/// once, plus each contract's own (explicitly allocated) non-labour cost.
/// </param>
/// <param name="DisputedCost">
/// The part of <paramref name="TotalCost"/> that more than one held-out
/// contract claims, counted once — the cost actually under dispute.
/// </param>
/// <param name="DuplicationIfSummed">
/// How much summing the held-out contracts' own cost figures would have
/// overstated <paramref name="TotalCost"/> by. This is **not** the same
/// quantity as <paramref name="DisputedCost"/> and must never be labelled as
/// if it were: cost claimed by three contracts is disputed once but inflates
/// a naive sum twice, so 100 appearing in three contracts gives a
/// DisputedCost of 100 and a DuplicationIfSummed of 200. Reported because it
/// is the evidence that the deduplication did something, not as a figure
/// anyone should read as money.
/// </param>
public sealed record ContestedCostRowViewModel(
    string Currency,
    decimal TotalCost,
    decimal DisputedCost,
    decimal DuplicationIfSummed,
    int ContractCount);

public sealed record CurrencyTotalRowViewModel(
    string Currency,
    MarginBasis MarginBasis,
    decimal TotalValue,
    decimal TotalCost,
    decimal Margin,
    decimal? MarginPercent,
    int ContractCount);

public sealed record CustomerMarginRowViewModel(
    string CustomerName,
    string Currency,
    MarginBasis MarginBasis,
    decimal TotalValue,
    decimal TotalCost,
    decimal Margin,
    decimal? MarginPercent,
    int ContractCount);

public sealed record CommercialModelMarginRowViewModel(
    CommercialModel CommercialModel,
    string Currency,
    MarginBasis MarginBasis,
    decimal TotalValue,
    decimal TotalCost,
    decimal Margin,
    decimal? MarginPercent,
    int ContractCount);
