using ProgrammePulse.Models.ViewModels.ContractOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// The cross-contract margin/profit rollup for the Contracts Overview page.
/// ContractCommercialService.BuildSummaryAsync already walks every contract
/// and computes its value/cost/margin — this only aggregates those rows, it
/// doesn't re-walk Customer -&gt; Programme -&gt; ... -&gt; TimeEntry itself.
/// Value isn't carried directly on ContractSummaryRowViewModel, only
/// CumulativeCost and Margin, so Value = CumulativeCost + Margin (valid
/// because Margin was computed as value - cost for every commercial model
/// in ContractCommercialService).
///
/// **Every total is grouped by MarginBasis as well as currency.** The basis
/// is what the number measures, and the three commercial models do not
/// produce the same measure: fixed-price margin is headroom against cost so
/// far with the cost to complete unknown, Ongoing is contracted value less
/// cost to date, and only Time &amp; Materials is near a realised margin. A
/// blended sum of those reads as portfolio profit and isn't, so this service
/// never produces one — the same reason it never blends across currencies.
///
/// What this page reports is therefore a rollup of the contracts whose cost
/// is attributable to them alone — not the whole book. Contracts sharing
/// cost with an overlapping contract are held out and counted, because a
/// portfolio margin that double-counts cost is worse than one that admits a
/// gap. The held-out cost itself comes from BuildSummaryAsync already
/// deduplicated, since summing those contracts' own cost figures would
/// repeat the double-count it exists to measure. See docs/contract-ops.md,
/// "Cost attribution".
/// </summary>
public sealed class PortfolioMarginService(IContractCommercialService contractCommercialService) : IPortfolioMarginService
{
    public async Task<PortfolioMarginViewModel> BuildAsync(Guid tenantId)
    {
        var summary = await contractCommercialService.BuildSummaryAsync(tenantId);

        // Two separate reasons a row can't be totalled, kept separate so the
        // page can say which one applies.
        //
        // 1. Contested attribution: another contract for the same customer
        //    overlaps this term, so both select the same hours. Adding them
        //    up double-counts the cost and overstates portfolio margin. Held
        //    out, and the held-out cost is reported per currency so the size
        //    of the gap is visible.
        // 2. A null Margin means revenue was never set (T&M with no BillRate)
        //    — excluded from every total, never assumed zero, the same
        //    honesty pattern as ContractCommercialService itself.
        var contestedCount = summary.Rows.Count(r => r.AttributionContested);
        var attributable = summary.Rows.Where(r => !r.AttributionContested).ToList();

        var priced = attributable.Where(r => r.Margin is not null).ToList();
        var mismatchedRevenueCount = attributable.Count(r => r.Margin is null);

        var totalsByCurrency = priced
            .GroupBy(r => (r.Currency, r.MarginBasis))
            .Select(g => new CurrencyTotalRowViewModel(
                g.Key.Currency,
                g.Key.MarginBasis,
                g.Sum(ValueOf),
                g.Sum(r => r.CumulativeCost),
                g.Sum(r => r.Margin!.Value),
                MarginPercent(g.Sum(ValueOf), g.Sum(r => r.Margin!.Value)),
                g.Count()))
            .OrderBy(r => r.Currency).ThenBy(r => r.MarginBasis)
            .ToList();

        var byCustomer = priced
            .GroupBy(r => (r.CustomerName, r.Currency, r.MarginBasis))
            .Select(g => new CustomerMarginRowViewModel(
                g.Key.CustomerName,
                g.Key.Currency,
                g.Key.MarginBasis,
                g.Sum(ValueOf),
                g.Sum(r => r.CumulativeCost),
                g.Sum(r => r.Margin!.Value),
                MarginPercent(g.Sum(ValueOf), g.Sum(r => r.Margin!.Value)),
                g.Count()))
            .OrderBy(r => r.CustomerName).ThenBy(r => r.Currency).ThenBy(r => r.MarginBasis)
            .ToList();

        // Basis is a function of commercial model for a priced row, so this
        // grouping is already basis-clean — it is grouped on explicitly
        // anyway, so that stays true if a fourth model ever shares a basis.
        var byCommercialModel = priced
            .GroupBy(r => (r.CommercialModel, r.Currency, r.MarginBasis))
            .Select(g => new CommercialModelMarginRowViewModel(
                g.Key.CommercialModel,
                g.Key.Currency,
                g.Key.MarginBasis,
                g.Sum(ValueOf),
                g.Sum(r => r.CumulativeCost),
                g.Sum(r => r.Margin!.Value),
                MarginPercent(g.Sum(ValueOf), g.Sum(r => r.Margin!.Value)),
                g.Count()))
            .OrderBy(r => r.CommercialModel).ThenBy(r => r.Currency).ThenBy(r => r.MarginBasis)
            .ToList();

        return new PortfolioMarginViewModel(
            totalsByCurrency, byCustomer, byCommercialModel,
            mismatchedRevenueCount, contestedCount, summary.ContestedCost);
    }

    private static decimal ValueOf(ContractSummaryRowViewModel row) =>
        row.CumulativeCost + row.Margin!.Value;

    private static decimal? MarginPercent(decimal value, decimal margin) =>
        value == 0 ? null : Math.Round(margin / value * 100m, 1);
}
