using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Computes the burn-down of contract value vs cost incurred — "how much is
/// booked against this, is it profitable". Walks Customer -> Programme ->
/// Project -> Workstream -> WorkItem -> TimeEntry, the same chain the
/// Customer rollup in ReportingQueryService already walks, then reuses
/// TimeEntryCostCalculator for the cost side.
///
/// **Cost attribution here is selection, not allocation.** A contract scopes
/// to one Customer and one term; there is no contract-to-work-item link in
/// the schema. Two contracts for the same customer whose terms overlap
/// therefore select the same hours, and summing their margins double-counts.
/// Rather than present that as profit, <see cref="ContractAttribution"/>
/// detects the overlap, the position is marked contested,
/// Margin/MarginPercent are suppressed, and the shared cost is reported so a
/// reviewer can go and look. Cost itself is still shown — the hours were
/// genuinely incurred for that customer; what is not established is that they
/// belong to this contract alone.
///
/// A time entry with no usable date (neither StartedAtUtc nor WorkDate) can
/// be placed in no term at all, so it is excluded from cost and reported as
/// UndatedHours instead of drifting into every contract for that customer.
/// Entries are term-filtered on TimeEntry.ReportDate (the provider's work
/// date, else the UTC start date), the same rule period reports and invoices
/// use, so the three never disagree about which day an hour belongs to.
///
/// ContractOps_* tables carry no TenantId column of their own and stay
/// single-tenant this cycle (see docs/tenancy.md's enforcement matrix), but
/// the IProgrammeReadRepository calls here now require one — the caller's
/// resolved tenantId is threaded through rather than a hardcoded constant,
/// so this doesn't quietly mix another tenant's programme/time-entry data
/// into a contract's numbers once a second tenant exists.
/// </summary>
public sealed class ContractCommercialService(
    IContractRepository contractRepository,
    IProgrammeReadRepository programmeRepository,
    IStaffRateRepository staffRateRepository) : IContractCommercialService
{
    /// <summary>A contract's time entries, split by whether they can be placed in its term at all.</summary>
    private readonly record struct ScopedEntries(
        IReadOnlyList<TimeEntry> InTerm,
        IReadOnlyList<TimeEntry> SharedWithOverlapping,
        decimal UndatedHours);

    /// <summary>
    /// A built position plus the inputs it was built from, kept together so
    /// BuildSummaryAsync can hand the contested contracts' time entries to
    /// ContestedCostAggregator without walking the book a second time.
    /// </summary>
    private readonly record struct PositionBuild(
        CommercialPositionViewModel Position,
        ScopedEntries Scoped,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> RateHistory);

    public async Task<ContractSummarySetViewModel> BuildSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Loaded once and threaded through: every position needs the full
        // contract list anyway, to work out which of them overlap it.
        var contracts = await contractRepository.GetContractsAsync(tenantId);
        var book = await LoadBookAsync(tenantId, cancellationToken);
        var rows = new List<ContractSummaryRowViewModel>();
        var contested = new List<ContestedCostAggregator.ContestedContractCost>();

        foreach (var contract in contracts)
        {
            var build = await BuildPositionAsync(contract, contracts, book, tenantId, cancellationToken);
            var position = build.Position;
            rows.Add(new ContractSummaryRowViewModel(
                position.ContractKey,
                position.Reference,
                position.CustomerName,
                position.CommercialModel,
                position.Status,
                position.Currency,
                position.ContractCeiling,
                position.CumulativeCost,
                position.Margin,
                position.MarginPercent,
                position.Attribution.Contested,
                position.MarginBasis));

            if (position.Attribution.Contested)
            {
                // The entries, not the total: two overlapping contracts hold
                // the same hours, and the held-out figure has to count them
                // once. NonLabourCostTotal is already this contract's own
                // matching-currency non-labour cost, which nothing else can
                // claim.
                contested.Add(new ContestedCostAggregator.ContestedContractCost(
                    position.Currency, build.Scoped.InTerm, build.RateHistory, position.NonLabourCostTotal));
            }
        }

        return new ContractSummarySetViewModel(
            rows.OrderBy(r => r.CustomerName).ThenBy(r => r.Reference).ToList(),
            ContestedCostAggregator.Aggregate(contested));
    }

    public async Task<CommercialPositionViewModel> BuildCommercialPositionAsync(Guid contractKey, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var contract = await contractRepository.GetContractByKeyAsync(contractKey, tenantId)
            ?? throw new InvalidOperationException($"Contract {contractKey} not found.");

        var allContracts = await contractRepository.GetContractsAsync(tenantId);
        return (await BuildPositionAsync(contract, allContracts, await LoadBookAsync(tenantId, cancellationToken), tenantId, cancellationToken)).Position;
    }

    private async Task<PositionBuild> BuildPositionAsync(
        Contract contract,
        IReadOnlyList<Contract> allContracts,
        TenantBook book,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var customerName = book.Customers.FirstOrDefault(c => c.CustomerKey == contract.CustomerKey)?.Name ?? "(unknown customer)";

        var overlapping = ContractAttribution.FindOverlapping(contract, allContracts);
        var scoped = GetScopedEntries(contract, overlapping, book);
        var rateHistory = await BuildRateHistoryAsync(scoped.InTerm, cancellationToken);
        var nonLabourCosts = await contractRepository.GetNonLabourCostsAsync(contract.ContractKey, tenantId);

        // Costed with the same calculator and currency rule as the headline
        // figure, so "of which shared" is directly comparable to cost to date.
        var sharedCost = TimeEntryCostCalculator
            .Calculate(scoped.SharedWithOverlapping, rateHistory, contract.Currency)
            .Cost;

        var attribution = overlapping.Count == 0
            ? ContractAttributionViewModel.Uncontested
            : new ContractAttributionViewModel(true, overlapping.Select(o => o.Reference).ToList(), sharedCost);

        var position = contract.CommercialModel switch
        {
            CommercialModel.Ongoing => BuildOngoingPosition(contract, customerName, scoped, rateHistory, nonLabourCosts, attribution),
            CommercialModel.FixedPrice => BuildFixedPricePosition(contract, customerName, scoped, rateHistory, nonLabourCosts, attribution),
            CommercialModel.TimeAndMaterials => BuildTimeAndMaterialsPosition(contract, customerName, scoped, rateHistory, nonLabourCosts, attribution),
            _ => throw new InvalidOperationException($"Unhandled commercial model {contract.CommercialModel}.")
        };

        return new PositionBuild(attribution.Contested ? SuppressMargin(position) : position, scoped, rateHistory);
    }

    /// <summary>
    /// Blanks the derived profit figures on a contested position. Cost,
    /// revenue and the yearly burn-down survive — those are selections a
    /// reviewer can verify. Margin does not, because it is the one number a
    /// reader would take as this contract's own profit.
    /// </summary>
    private static CommercialPositionViewModel SuppressMargin(CommercialPositionViewModel position) =>
        position with { Margin = null, MarginPercent = null, MarginBasis = MarginBasis.NotEstablished };

    /// <summary>
    /// Splits non-labour costs the same way TimeEntryCostCalculator splits
    /// labour cost: matching-currency entries are summed into the total,
    /// a different-currency entry is excluded and counted separately rather
    /// than silently summed across currencies.
    /// </summary>
    private static (decimal Matching, decimal Mismatched) SplitNonLabourCosts(IEnumerable<NonLabourCost> costs, string contractCurrency)
    {
        decimal matching = 0m;
        decimal mismatched = 0m;
        foreach (var cost in costs)
        {
            if (string.Equals(cost.Currency, contractCurrency, StringComparison.OrdinalIgnoreCase))
            {
                matching += cost.Amount;
            }
            else
            {
                mismatched += cost.Amount;
            }
        }

        return (matching, mismatched);
    }

    private static List<NonLabourCostRowViewModel> ToRows(IReadOnlyList<NonLabourCost> costs) =>
        costs.Select(c => new NonLabourCostRowViewModel(c.NonLabourCostKey, c.Description, c.Amount, c.Currency, c.IncurredOn)).ToList();

    /// <summary>
    /// The date an entry is attributed to: <see cref="TimeEntry.ReportDate"/>,
    /// the provider's work date, else the UTC start date. Period reports and
    /// invoices use the same rule (decision 4), so a contract's cost, its
    /// reports and its invoices all place an hour on the same day.
    /// </summary>
    private static DateOnly? EffectiveDate(TimeEntry entry) => entry.ReportDate;

    /// <summary>
    /// The tenant's customers and each customer's time entries, walked once
    /// (Programme -> Project -> Workstream -> WorkItem -> TimeEntry) per
    /// request. The portfolio summary used to repeat the whole walk, and read
    /// every time entry again, once per contract.
    /// </summary>
    private sealed record TenantBook(IReadOnlyList<Customer> Customers, ILookup<Guid, TimeEntry> EntriesByCustomer);

    private async Task<TenantBook> LoadBookAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var customerByProgramme = (await programmeRepository.GetProgrammesAsync(tenantId, cancellationToken))
            .Where(p => p.CustomerKey is not null)
            .ToDictionary(p => p.ProgrammeKey, p => p.CustomerKey!.Value);
        var customerByProject = (await programmeRepository.GetProjectsAsync(tenantId, cancellationToken))
            .Where(p => customerByProgramme.ContainsKey(p.ProgrammeKey))
            .ToDictionary(p => p.ProjectKey, p => customerByProgramme[p.ProgrammeKey]);
        var customerByWorkstream = (await programmeRepository.GetWorkstreamsAsync(tenantId, cancellationToken))
            .Where(w => customerByProject.ContainsKey(w.ProjectKey))
            .ToDictionary(w => w.WorkstreamKey, w => customerByProject[w.ProjectKey]);
        var customerByWorkItem = (await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken))
            .Where(w => customerByWorkstream.ContainsKey(w.WorkstreamKey))
            .ToDictionary(w => w.WorkItemKey, w => customerByWorkstream[w.WorkstreamKey]);

        var entriesByCustomer = (await programmeRepository.GetTimeEntriesAsync(tenantId, cancellationToken))
            .Where(t => t.WorkItemKey is { } key && customerByWorkItem.ContainsKey(key))
            .ToLookup(t => customerByWorkItem[t.WorkItemKey!.Value]);

        return new TenantBook(await programmeRepository.GetCustomersAsync(tenantId, cancellationToken), entriesByCustomer);
    }

    private static ScopedEntries GetScopedEntries(Contract contract, IReadOnlyList<Contract> overlapping, TenantBook book)
    {
        var customerEntries = book.EntriesByCustomer[contract.CustomerKey];

        var inTerm = new List<TimeEntry>();
        var shared = new List<TimeEntry>();
        var undatedHours = 0m;

        foreach (var entry in customerEntries)
        {
            var date = EffectiveDate(entry);
            if (date is null)
            {
                // Belongs to this customer but to no identifiable period, so
                // it can be placed in no contract term. Reported, not costed.
                undatedHours += entry.DurationHours;
                continue;
            }

            if (!ContractAttribution.ContainsDate(contract, date.Value))
            {
                continue;
            }

            inTerm.Add(entry);
            if (overlapping.Any(other => ContractAttribution.ContainsDate(other, date.Value)))
            {
                shared.Add(entry);
            }
        }

        return new ScopedEntries(inTerm, shared, undatedHours);
    }

    private Task<IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>>> BuildRateHistoryAsync(IReadOnlyList<TimeEntry> entries, CancellationToken cancellationToken) =>
        staffRateRepository.GetHistoryAsync(entries.Where(e => e.StaffKey is not null).Select(e => e.StaffKey!.Value).Distinct().ToList(), cancellationToken);

    /// <summary>
    /// One row per contract-year, running cumulative totals — the exact
    /// "£350k/year over 5 years, how much booked, is it profitable" view.
    /// </summary>
    private static CommercialPositionViewModel BuildOngoingPosition(
        Contract contract,
        string customerName,
        ScopedEntries scoped,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> rateHistory,
        IReadOnlyList<NonLabourCost> nonLabourCosts,
        ContractAttributionViewModel attribution)
    {
        var annualValue = contract.AnnualValue ?? 0m;

        var totalUnpriced = 0m;
        var totalMismatched = 0m;
        var totalMismatchedNonLabour = 0m;

        var years = new List<ContractYearRowViewModel>();
        var cumulativeValue = 0m;
        var cumulativeCost = 0m;
        var yearNumber = 1;
        var cursor = contract.StartDate;

        while (cursor < contract.EndDate)
        {
            var yearEnd = cursor.AddYears(1);
            if (yearEnd > contract.EndDate)
            {
                yearEnd = contract.EndDate;
            }

            var yearEntries = scoped.InTerm
                .Where(e => EffectiveDate(e) is { } d && d >= cursor && d < yearEnd)
                .ToList();
            var result = TimeEntryCostCalculator.Calculate(yearEntries, rateHistory, contract.Currency);

            var yearNonLabourCosts = nonLabourCosts.Where(c => c.IncurredOn >= cursor && c.IncurredOn < yearEnd);
            var (yearNonLabourMatching, yearNonLabourMismatched) = SplitNonLabourCosts(yearNonLabourCosts, contract.Currency);

            totalUnpriced += result.UnpricedHours;
            totalMismatched += result.MismatchedCurrencyHours;
            totalMismatchedNonLabour += yearNonLabourMismatched;
            cumulativeValue += annualValue;
            var yearCost = result.Cost + yearNonLabourMatching;
            cumulativeCost += yearCost;

            years.Add(new ContractYearRowViewModel(
                yearNumber, cursor, yearEnd, annualValue, yearCost, annualValue - yearCost,
                cumulativeValue, cumulativeCost, cumulativeValue - cumulativeCost));

            cursor = yearEnd;
            yearNumber++;
        }

        var ceiling = contract.AnnualValue.HasValue ? annualValue * (yearNumber - 1) : (decimal?)null;
        var margin = cumulativeValue - cumulativeCost;
        var nonLabourTotal = nonLabourCosts.Sum(c => c.Amount) - totalMismatchedNonLabour;

        return new CommercialPositionViewModel(
            contract.ContractKey, contract.Reference, customerName, contract.CommercialModel, contract.Status,
            contract.Currency, ceiling, cumulativeCost, cumulativeValue, margin,
            cumulativeValue == 0 ? null : Math.Round(margin / cumulativeValue * 100m, 1),
            RevenueUnset: false, totalUnpriced, totalMismatched, years,
            nonLabourTotal, totalMismatchedNonLabour, ToRows(nonLabourCosts),
            attribution, MarginBasis.ContractedValueLessCostToDate, scoped.UndatedHours);
    }

    /// <summary>
    /// A fixed-price engagement isn't necessarily an annually recurring
    /// amount, so no artificial per-year split — one overall figure: total
    /// value vs cumulative cost to date. The difference is headroom
    /// remaining, not margin: the cost still to be incurred completing the
    /// scope is unknown here. See <see cref="MarginBasis"/>.
    /// </summary>
    private static CommercialPositionViewModel BuildFixedPricePosition(
        Contract contract,
        string customerName,
        ScopedEntries scoped,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> rateHistory,
        IReadOnlyList<NonLabourCost> nonLabourCosts,
        ContractAttributionViewModel attribution)
    {
        var result = TimeEntryCostCalculator.Calculate(scoped.InTerm, rateHistory, contract.Currency);
        var (nonLabourMatching, nonLabourMismatched) = SplitNonLabourCosts(nonLabourCosts, contract.Currency);
        var totalCost = result.Cost + nonLabourMatching;
        var margin = contract.TotalContractValue.HasValue ? contract.TotalContractValue.Value - totalCost : (decimal?)null;

        return new CommercialPositionViewModel(
            contract.ContractKey, contract.Reference, customerName, contract.CommercialModel, contract.Status,
            contract.Currency, contract.TotalContractValue, totalCost, contract.TotalContractValue, margin,
            margin.HasValue && contract.TotalContractValue is > 0 ? Math.Round(margin.Value / contract.TotalContractValue.Value * 100m, 1) : null,
            RevenueUnset: false, result.UnpricedHours, result.MismatchedCurrencyHours, [],
            nonLabourMatching, nonLabourMismatched, ToRows(nonLabourCosts),
            attribution,
            margin.HasValue ? MarginBasis.CostToDateHeadroom : MarginBasis.NotEstablished,
            scoped.UndatedHours);
    }

    /// <summary>
    /// Revenue = billable hours x BillRate. If BillRate was never set,
    /// revenue is reported as unset rather than assumed zero — the same
    /// honesty pattern as unpriced cost entries.
    /// </summary>
    private static CommercialPositionViewModel BuildTimeAndMaterialsPosition(
        Contract contract,
        string customerName,
        ScopedEntries scoped,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> rateHistory,
        IReadOnlyList<NonLabourCost> nonLabourCosts,
        ContractAttributionViewModel attribution)
    {
        var result = TimeEntryCostCalculator.Calculate(scoped.InTerm, rateHistory, contract.Currency);
        var (nonLabourMatching, nonLabourMismatched) = SplitNonLabourCosts(nonLabourCosts, contract.Currency);
        var totalCost = result.Cost + nonLabourMatching;
        var revenueUnset = contract.BillRate is null;
        var revenue = contract.BillRate.HasValue ? result.BillableHours * contract.BillRate.Value : (decimal?)null;
        var margin = revenue.HasValue ? revenue.Value - totalCost : (decimal?)null;

        return new CommercialPositionViewModel(
            contract.ContractKey, contract.Reference, customerName, contract.CommercialModel, contract.Status,
            contract.Currency, null, totalCost, revenue, margin,
            margin.HasValue && revenue is > 0 ? Math.Round(margin.Value / revenue.Value * 100m, 1) : null,
            revenueUnset, result.UnpricedHours, result.MismatchedCurrencyHours, [],
            nonLabourMatching, nonLabourMismatched, ToRows(nonLabourCosts),
            attribution,
            margin.HasValue ? MarginBasis.ChargeableValueLessCostToDate : MarginBasis.NotEstablished,
            scoped.UndatedHours);
    }
}
