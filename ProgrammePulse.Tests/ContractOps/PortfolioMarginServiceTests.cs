using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ContractOps;

namespace ProgrammePulse.Tests.ContractOps;

public class PortfolioMarginServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ContractSummaryRowViewModel Row(
        string customerName,
        CommercialModel commercialModel,
        string currency,
        decimal cost,
        decimal? margin,
        bool contested = false,
        MarginBasis? basis = null) =>
        new(Guid.NewGuid(), $"REF-{Guid.NewGuid():N}", customerName, commercialModel, ContractStatus.Active,
            currency, null, cost, margin, null, contested,
            margin is null ? MarginBasis.NotEstablished : basis ?? MarginBasis.CostToDateHeadroom);

    [Fact]
    public async Task BuildAsync_KeepsCurrenciesSeparate_NeverBlendsATotalAcrossThem()
    {
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: 500m),
            Row("Acme", CommercialModel.FixedPrice, "USD", cost: 2000m, margin: 1000m)
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        Assert.Equal(2, result.TotalsByCurrency.Count);
        var gbp = Assert.Single(result.TotalsByCurrency, r => r.Currency == "GBP");
        var usd = Assert.Single(result.TotalsByCurrency, r => r.Currency == "USD");
        Assert.Equal(500m, gbp.Margin);
        Assert.Equal(1000m, usd.Margin);
    }

    [Fact]
    public async Task BuildAsync_KeepsMarginBasesSeparate_NeverBlendsHeadroomWithRealisedMargin()
    {
        // The CFO gate: fixed-price headroom is value less cost so far with
        // the cost to complete unknown; chargeable-less-cost is near a real
        // margin. One currency, but two different measures — a single
        // "portfolio margin of 900" would be a number no one can act on.
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: 500m, basis: MarginBasis.CostToDateHeadroom),
            Row("Beta", CommercialModel.TimeAndMaterials, "GBP", cost: 600m, margin: 400m, basis: MarginBasis.ChargeableValueLessCostToDate)
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        Assert.Equal(2, result.TotalsByCurrency.Count);
        Assert.DoesNotContain(result.TotalsByCurrency, r => r.Margin == 900m);

        var headroom = Assert.Single(result.TotalsByCurrency, r => r.MarginBasis == MarginBasis.CostToDateHeadroom);
        Assert.Equal(500m, headroom.Margin);
        Assert.Equal(1500m, headroom.TotalValue);

        var chargeable = Assert.Single(result.TotalsByCurrency, r => r.MarginBasis == MarginBasis.ChargeableValueLessCostToDate);
        Assert.Equal(400m, chargeable.Margin);
        Assert.Equal(1000m, chargeable.TotalValue);
    }

    [Fact]
    public async Task BuildAsync_SplitsTheCustomerRollupByBasisToo_SoNoSubTotalReblendsThem()
    {
        // One customer holding a fixed-price and an Ongoing contract is the
        // easy place for the distinction to leak back in.
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: 500m, basis: MarginBasis.CostToDateHeadroom),
            Row("Acme", CommercialModel.Ongoing, "GBP", cost: 3000m, margin: 1000m, basis: MarginBasis.ContractedValueLessCostToDate)
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        var acme = result.ByCustomer.Where(r => r.CustomerName == "Acme").ToList();
        Assert.Equal(2, acme.Count);
        Assert.All(acme, r => Assert.Equal(1, r.ContractCount));
        Assert.DoesNotContain(acme, r => r.Margin == 1500m);
        Assert.Equal(500m, Assert.Single(acme, r => r.MarginBasis == MarginBasis.CostToDateHeadroom).Margin);
        Assert.Equal(1000m, Assert.Single(acme, r => r.MarginBasis == MarginBasis.ContractedValueLessCostToDate).Margin);
    }

    [Fact]
    public async Task BuildAsync_ExcludesRevenueUnsetContracts_FromTotals_AndCountsThemSeparately()
    {
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.TimeAndMaterials, "GBP", cost: 1000m, margin: 500m),
            Row("Beta", CommercialModel.TimeAndMaterials, "GBP", cost: 800m, margin: null) // BillRate unset
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        var gbp = Assert.Single(result.TotalsByCurrency);
        Assert.Equal(1, gbp.ContractCount);
        Assert.Equal(500m, gbp.Margin);
        Assert.Equal(1, result.MismatchedRevenueContractCount);
    }

    [Fact]
    public async Task BuildAsync_ExcludesContestedAttributionContracts_FromTotals_AndReportsTheHeldOutCost()
    {
        // The finance-demo gate: a contract whose cost is shared with an
        // overlapping contract for the same customer must not contribute to a
        // portfolio margin, because its cost is also counted in the other one.
        //
        // The held-out figure itself comes from ContractCommercialService
        // already deduplicated — the two Beta contracts below each report
        // 4,000 of cost but share the same hours, so 4,000 is held out, not
        // 8,000. See ContestedCostAggregatorTests for that arithmetic.
        var summary = new FakeContractCommercialService(
            [
                Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: 500m),
                Row("Beta", CommercialModel.FixedPrice, "GBP", cost: 4000m, margin: 6000m, contested: true),
                Row("Beta", CommercialModel.FixedPrice, "GBP", cost: 4000m, margin: 6000m, contested: true)
            ],
            [new ContestedCostRowViewModel("GBP", TotalCost: 4000m, DisputedCost: 4000m, DuplicationIfSummed: 4000m, ContractCount: 2)]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        var gbp = Assert.Single(result.TotalsByCurrency);
        Assert.Equal(1, gbp.ContractCount);
        Assert.Equal(500m, gbp.Margin);          // not 12,500 — the 12,000 of contested margin is held out
        Assert.Equal(1000m, gbp.TotalCost);

        Assert.Equal(2, result.AttributionContestedContractCount);
        var held = Assert.Single(result.ContestedCost);
        Assert.Equal("GBP", held.Currency);
        Assert.Equal(4000m, held.TotalCost);     // not 8,000 — the shared hours are counted once
        Assert.Equal(2, held.ContractCount);

        // Held-out contracts leave the customer and model breakdowns too, so no
        // sub-total silently reintroduces the double-counted figure.
        Assert.DoesNotContain(result.ByCustomer, r => r.CustomerName == "Beta");
        Assert.Equal(500m, result.ByCommercialModel.Single().Margin);
    }

    [Fact]
    public async Task BuildAsync_CountsAContestedContract_AsContested_NotAsUnsetRevenue()
    {
        // A contested contract has its Margin suppressed upstream, which would
        // otherwise make it look like a T&M contract with no bill rate. The two
        // reasons stay distinct so the page can say which one applies.
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: null, contested: true)
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        Assert.Equal(1, result.AttributionContestedContractCount);
        Assert.Equal(0, result.MismatchedRevenueContractCount);
    }

    [Fact]
    public async Task BuildAsync_GroupsByCustomerAndByCommercialModel_PerCurrency()
    {
        var summary = new FakeContractCommercialService(
        [
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 1000m, margin: 500m),
            Row("Acme", CommercialModel.FixedPrice, "GBP", cost: 3000m, margin: 1000m),
            Row("Beta", CommercialModel.FixedPrice, "GBP", cost: 500m, margin: 200m)
        ]);
        var sut = new PortfolioMarginService(summary);

        var result = await sut.BuildAsync(TestTenantId);

        var acme = result.ByCustomer.Single(r => r.CustomerName == "Acme");
        Assert.Equal(2, acme.ContractCount);
        Assert.Equal(1500m, acme.Margin);

        var fixedPrice = result.ByCommercialModel.Single(r => r.CommercialModel == CommercialModel.FixedPrice);
        Assert.Equal(3, fixedPrice.ContractCount);
        Assert.Equal(1700m, fixedPrice.Margin);
    }

    private sealed class FakeContractCommercialService(
        IReadOnlyList<ContractSummaryRowViewModel> rows,
        IReadOnlyList<ContestedCostRowViewModel>? contestedCost = null) : IContractCommercialService
    {
        public Task<ContractSummarySetViewModel> BuildSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContractSummarySetViewModel(rows, contestedCost ?? []));

        public Task<CommercialPositionViewModel> BuildCommercialPositionAsync(Guid contractKey, Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by PortfolioMarginService.");
    }
}
