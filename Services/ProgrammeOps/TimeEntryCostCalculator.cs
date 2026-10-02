using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

public readonly record struct TimeEntryCostResult(
    decimal Cost,
    decimal PricedHours,
    decimal BillableHours,
    decimal UnpricedHours,
    decimal MismatchedCurrencyHours,
    string? Currency);

/// <summary>
/// The TimeEntry-to-StaffRate join originally built for
/// ReportingQueryService.BuildCostSummaryAsync — extracted so
/// ContractCommercialService (a different feature area) can reuse the exact
/// same rate-matching logic rather than a second copy of it. Joins each
/// entry to the StaffRate effective on its known start instant or work date (history, not
/// just the current rate, so a past rate change doesn't retroactively
/// re-cost old entries).
/// </summary>
public static class TimeEntryCostCalculator
{
    /// <param name="requiredCurrency">
    /// When set, an entry whose effective rate is in a different currency is
    /// excluded from Cost and counted in MismatchedCurrencyHours instead of
    /// being silently summed across currencies. Null (the Cost Summary
    /// caller's default) preserves the original single-currency-assumed
    /// behaviour exactly.
    /// </param>
    public static TimeEntryCostResult Calculate(
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>> rateHistoryByStaff,
        string? requiredCurrency = null)
    {
        decimal cost = 0m;
        decimal pricedHours = 0m;
        decimal billableHours = 0m;
        decimal unpricedHours = 0m;
        decimal mismatchedCurrencyHours = 0m;
        string? currency = null;

        foreach (var entry in entries)
        {
            if (entry.StaffKey is null || (entry.StartedAtUtc is null && entry.WorkDate is null))
            {
                unpricedHours += entry.DurationHours;
                continue;
            }

            if (!rateHistoryByStaff.TryGetValue(entry.StaffKey.Value, out var history))
            {
                unpricedHours += entry.DurationHours;
                continue;
            }

            var entryDate = entry.StartedAtUtc ?? entry.WorkDate!.Value.ToDateTime(TimeOnly.MinValue);
            var rate = history.FirstOrDefault(r => r.EffectiveFromUtc <= entryDate && (r.EffectiveToUtc is null || r.EffectiveToUtc > entryDate));
            if (rate is null)
            {
                unpricedHours += entry.DurationHours;
                continue;
            }

            if (requiredCurrency is not null && !string.Equals(rate.RateCurrency, requiredCurrency, StringComparison.OrdinalIgnoreCase))
            {
                mismatchedCurrencyHours += entry.DurationHours;
                continue;
            }

            cost += entry.DurationHours * rate.CostPerHour;
            pricedHours += entry.DurationHours;
            if (entry.BillabilityKnown && entry.IsBillable)
            {
                billableHours += entry.DurationHours;
            }
            currency ??= rate.RateCurrency;
        }

        return new TimeEntryCostResult(cost, pricedHours, billableHours, unpricedHours, mismatchedCurrencyHours, currency);
    }
}
