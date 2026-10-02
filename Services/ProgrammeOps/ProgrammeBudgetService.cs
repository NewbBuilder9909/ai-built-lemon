using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Programme-level budget vs actual — extends Contract Ops' contract-scoped
/// burn-down to a programme that has no formal Contract record. Walks
/// Programme -&gt; Project -&gt; Workstream -&gt; WorkItem -&gt; TimeEntry (a narrower
/// version of the same shape ContractCommercialService and
/// InvoiceGenerationService walk, scoped to one Programme directly instead
/// of fanning out from a Customer) and reuses TimeEntryCostCalculator for
/// the cost side, same currency-mismatch-excluded-and-surfaced pattern.
/// </summary>
public sealed class ProgrammeBudgetService(
    IProgrammeReadRepository programmeRepository,
    IStaffRateRepository staffRateRepository) : IProgrammeBudgetService
{
    public async Task<IReadOnlyList<ProgrammeBudgetRowViewModel>> BuildBudgetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var programmes = await programmeRepository.GetProgrammesAsync(tenantId, cancellationToken);
        var projects = await programmeRepository.GetProjectsAsync(tenantId, cancellationToken);
        var workstreams = await programmeRepository.GetWorkstreamsAsync(tenantId, cancellationToken);
        var workItems = await programmeRepository.GetWorkItemsAsync(tenantId, cancellationToken);
        var timeEntries = await programmeRepository.GetTimeEntriesAsync(tenantId, cancellationToken);
        var customers = await programmeRepository.GetCustomersAsync(tenantId, cancellationToken);

        var projectsByProgramme = projects.ToLookup(p => p.ProgrammeKey);
        var workstreamsByProject = workstreams.ToLookup(w => w.ProjectKey);
        var workItemsByWorkstream = workItems.ToLookup(w => w.WorkstreamKey);
        var entriesByWorkItem = timeEntries.Where(t => t.WorkItemKey is not null).ToLookup(t => t.WorkItemKey!.Value);
        var customerNamesByKey = customers.ToDictionary(c => c.CustomerKey, c => c.Name);

        // One rate read for every programme, not one per programme per person.
        var rateHistory = await BuildRateHistoryAsync(timeEntries, cancellationToken);

        var rows = new List<ProgrammeBudgetRowViewModel>();
        foreach (var programme in programmes)
        {
            var scopedEntries = projectsByProgramme[programme.ProgrammeKey]
                .SelectMany(p => workstreamsByProject[p.ProjectKey])
                .SelectMany(w => workItemsByWorkstream[w.WorkstreamKey])
                .SelectMany(i => entriesByWorkItem[i.WorkItemKey])
                .ToList();

            var result = TimeEntryCostCalculator.Calculate(scopedEntries, rateHistory, programme.BudgetCurrency);

            var burnPercent = programme.BudgetAmount is > 0m
                ? Math.Round(result.Cost / programme.BudgetAmount.Value * 100m, 1)
                : (decimal?)null;

            rows.Add(new ProgrammeBudgetRowViewModel(
                programme.ProgrammeKey,
                programme.Name,
                programme.CustomerKey is Guid customerKey ? customerNamesByKey.GetValueOrDefault(customerKey) : null,
                programme.BudgetAmount,
                programme.BudgetCurrency,
                result.Cost,
                burnPercent,
                result.UnpricedHours,
                result.MismatchedCurrencyHours));
        }

        return rows
            .OrderByDescending(r => r.BudgetAmount.HasValue)
            .ThenBy(r => r.ProgrammeName)
            .ToList();
    }

    private Task<IReadOnlyDictionary<Guid, IReadOnlyList<StaffRate>>> BuildRateHistoryAsync(IReadOnlyList<TimeEntry> entries, CancellationToken cancellationToken) =>
        staffRateRepository.GetHistoryAsync(entries.Where(e => e.StaffKey is not null).Select(e => e.StaffKey!.Value).Distinct().ToList(), cancellationToken);
}
