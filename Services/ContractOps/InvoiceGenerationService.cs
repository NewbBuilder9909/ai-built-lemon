using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Turns a period's billable Time &amp; Materials hours (and any non-labour
/// costs incurred in it) into a <b>draft</b> invoice for review. Fixed Price
/// and Ongoing contracts can still invoice non-labour costs; they get no
/// hours line since their value isn't hours-based.
///
/// Decision 4 of docs/delivery-evidence-and-contract-assurance.md sets three
/// rules here:
///
/// - **One date rule.** An entry belongs to the period its
///   <see cref="TimeEntry.ReportDate"/> falls in (the provider's work date,
///   else the UTC start date): the same date period reports and contract
///   costing use, so an invoice reconciles to the margin it came from. The
///   old rule (UTC start date only) never invoiced Tempo time at all.
/// - **Only known billability is billed.** Hours whose source gave no
///   billability are not billed and not dropped: they are recorded on the
///   draft as needing review.
/// - **Billing doesn't depend on cost rates.** Hours are billed at the
///   contract's bill rate whether or not the person has a cost rate. The
///   previous version counted billable hours only for priced entries, so a
///   person with no cost rate on file was never invoiced.
///
/// The Customer → Programme → Project → Workstream → WorkItem walk mirrors
/// ContractCommercialService's, bound to the invoice period rather than the
/// contract term.
/// </summary>
public sealed class InvoiceGenerationService(
    IContractRepository contractRepository,
    IProgrammeReadRepository programmeRepository,
    TimeProvider timeProvider) : IInvoiceGenerationService
{
    public async Task<Invoice> GenerateAsync(Guid contractKey, DateOnly periodStart, DateOnly periodEnd, Guid? generatedByStaffKey, Guid tenantId)
    {
        if (periodEnd < periodStart)
        {
            throw new InvoiceGenerationException("The period ends before it starts.");
        }

        var contract = await contractRepository.GetContractByKeyAsync(contractKey, tenantId)
            ?? throw new InvalidOperationException($"Contract {contractKey} not found.");

        var invoiceKey = Guid.NewGuid();
        var lines = new List<InvoiceLine>();
        var needsReviewHours = 0m;

        if (contract.CommercialModel == CommercialModel.TimeAndMaterials && contract.BillRate is decimal billRate)
        {
            var entries = await GetTimeEntriesInPeriodAsync(contract, periodStart, periodEnd, tenantId);
            var billableHours = entries.Where(e => e.BillabilityKnown && e.IsBillable).Sum(e => e.DurationHours);
            needsReviewHours = entries.Where(e => !e.BillabilityKnown).Sum(e => e.DurationHours);

            if (billableHours > 0m)
            {
                lines.Add(new InvoiceLine
                {
                    InvoiceLineKey = Guid.NewGuid(),
                    InvoiceKey = invoiceKey,
                    Description = $"Professional services, {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
                    Quantity = billableHours,
                    UnitRate = billRate,
                    LineTotal = billableHours * billRate
                });
            }
        }

        var nonLabourCosts = (await contractRepository.GetNonLabourCostsAsync(contractKey, tenantId))
            .Where(c => c.IncurredOn >= periodStart && c.IncurredOn <= periodEnd)
            .Where(c => string.Equals(c.Currency, contract.Currency, StringComparison.OrdinalIgnoreCase));

        foreach (var cost in nonLabourCosts)
        {
            lines.Add(new InvoiceLine
            {
                InvoiceLineKey = Guid.NewGuid(),
                InvoiceKey = invoiceKey,
                Description = cost.Description,
                Quantity = null,
                UnitRate = null,
                LineTotal = cost.Amount
            });
        }

        if (lines.Count == 0)
        {
            throw new InvoiceGenerationException(needsReviewHours > 0m
                ? $"Nothing can be invoiced for this period yet: {needsReviewHours:0.##} hours have no confirmed billability. Confirm billability at the source, then generate again."
                : "Nothing to invoice for this period — no billable Time & Materials hours and no non-labour costs incurred.");
        }

        var invoice = new Invoice
        {
            InvoiceKey = invoiceKey,
            ContractKey = contractKey,
            InvoiceNumber = Invoice.DraftNumber(invoiceKey),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Currency = contract.Currency,
            Subtotal = lines.Sum(l => l.LineTotal),
            Status = InvoiceStatus.Draft,
            GeneratedByStaffKey = generatedByStaffKey,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            NeedsReviewHours = needsReviewHours,
        };

        return await contractRepository.CreateInvoiceAsync(invoice, lines, tenantId);
    }

    private async Task<List<TimeEntry>> GetTimeEntriesInPeriodAsync(Contract contract, DateOnly periodStart, DateOnly periodEnd, Guid tenantId)
    {
        var programmes = (await programmeRepository.GetProgrammesAsync(tenantId))
            .Where(p => p.CustomerKey == contract.CustomerKey)
            .Select(p => p.ProgrammeKey)
            .ToHashSet();

        var projectKeys = (await programmeRepository.GetProjectsAsync(tenantId))
            .Where(p => programmes.Contains(p.ProgrammeKey))
            .Select(p => p.ProjectKey)
            .ToHashSet();

        var workstreamKeys = (await programmeRepository.GetWorkstreamsAsync(tenantId))
            .Where(w => projectKeys.Contains(w.ProjectKey))
            .Select(w => w.WorkstreamKey)
            .ToHashSet();

        var workItemKeys = (await programmeRepository.GetWorkItemsAsync(tenantId))
            .Where(w => workstreamKeys.Contains(w.WorkstreamKey))
            .Select(w => w.WorkItemKey)
            .ToHashSet();

        // The period read selects on ReportDate, which is the invoice's date rule.
        return (await programmeRepository.GetTimeEntriesAsync(tenantId, periodStart, periodEnd))
            .Where(t => t.WorkItemKey.HasValue && workItemKeys.Contains(t.WorkItemKey.Value))
            .ToList();
    }
}
