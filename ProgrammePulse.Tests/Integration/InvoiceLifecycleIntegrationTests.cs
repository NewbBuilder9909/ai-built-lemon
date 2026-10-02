using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ContractOps;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// The invoice draft stage against real SQL Server (ContractOps step 06):
/// the new columns round-trip; issuing assigns the next number only to a
/// draft, counting only numbered invoices; concurrent issues on one contract
/// never share a number; and another tenant can neither issue nor move it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class InvoiceLifecycleIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the invoice draft stage was not exercised against real SQL Server.";
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private T Resolve<T>() where T : notnull => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Drafts_are_numbered_at_issue_and_only_once()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IContractRepository>();
        var tenant = Guid.NewGuid();
        var (contract, reference) = await SeedContractAsync(tenant);

        var discarded = await CreateDraftAsync(repository, contract, tenant, needsReviewHours: 0m);
        var first = await CreateDraftAsync(repository, contract, tenant, needsReviewHours: 6.5m);
        var second = await CreateDraftAsync(repository, contract, tenant, needsReviewHours: 0m);

        var stored = (await repository.GetInvoiceAsync(contract, first, tenant))!.Value.Invoice;
        Assert.Equal((InvoiceStatus.Draft, 6.5m, (DateTime?)null), (stored.Status, stored.NeedsReviewHours, stored.IssuedAtUtc));

        Assert.True(await repository.MoveInvoiceAsync(discarded, InvoiceStatus.Draft, InvoiceStatus.Discarded, tenant));
        Assert.Null(await repository.IssueInvoiceAsync(contract, first, reference, Now, Guid.NewGuid()));
        Assert.Equal($"{reference}-001", await repository.IssueInvoiceAsync(contract, first, reference, Now, tenant));
        Assert.Null(await repository.IssueInvoiceAsync(contract, first, reference, Now, tenant));
        Assert.Null(await repository.IssueInvoiceAsync(contract, discarded, reference, Now, tenant));
        Assert.Equal($"{reference}-002", await repository.IssueInvoiceAsync(contract, second, reference, Now, tenant));

        var issued = (await repository.GetInvoiceAsync(contract, first, tenant))!.Value.Invoice;
        Assert.Equal((InvoiceStatus.Issued, Now), (issued.Status, issued.IssuedAtUtc!.Value));

        Assert.False(await repository.MoveInvoiceAsync(first, InvoiceStatus.Issued, InvoiceStatus.Paid, Guid.NewGuid()));
        Assert.False(await repository.MoveInvoiceAsync(first, InvoiceStatus.Draft, InvoiceStatus.Discarded, tenant));
        Assert.True(await repository.MoveInvoiceAsync(first, InvoiceStatus.Issued, InvoiceStatus.Paid, tenant));
    }

    [Fact]
    public async Task Concurrent_issues_on_one_contract_never_share_a_number()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenant = Guid.NewGuid();
        var (contract, reference) = await SeedContractAsync(tenant);
        var drafts = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            drafts.Add(await CreateDraftAsync(Resolve<IContractRepository>(), contract, tenant, 0m));
        }

        var numbers = await Task.WhenAll(drafts.Select(d =>
            Task.Run(() => Resolve<IContractRepository>().IssueInvoiceAsync(contract, d, reference, Now, tenant))));

        Assert.All(numbers, Assert.NotNull);
        Assert.Equal(
            Enumerable.Range(1, 6).Select(n => $"{reference}-{n:D3}"),
            numbers.Order(StringComparer.Ordinal));
    }

    private static async Task<Guid> CreateDraftAsync(IContractRepository repository, Guid contract, Guid tenant, decimal needsReviewHours)
    {
        var key = Guid.NewGuid();
        await repository.CreateInvoiceAsync(new Invoice
        {
            InvoiceKey = key, ContractKey = contract, InvoiceNumber = Invoice.DraftNumber(key),
            PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = new DateOnly(2026, 6, 30), Currency = "GBP", Subtotal = 100m,
            Status = InvoiceStatus.Draft, CreatedAtUtc = Now, NeedsReviewHours = needsReviewHours
        },
        [new InvoiceLine { InvoiceLineKey = Guid.NewGuid(), InvoiceKey = key, Description = "Services", LineTotal = 100m }],
        tenant);
        return key;
    }

    private async Task<(Guid Contract, string Reference)> SeedContractAsync(Guid tenant)
    {
        var customer = await Resolve<ProgrammePulse.Services.ProgrammeOps.IProgrammeRepository>().UpsertCustomerAsync(new Customer
        {
            CustomerKey = Guid.NewGuid(), Name = "Invoice customer", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        var reference = $"IN-{Guid.NewGuid():N}"[..16];
        var contract = await Resolve<IContractRepository>().CreateContractAsync(new Contract
        {
            ContractKey = Guid.NewGuid(), CustomerKey = customer.CustomerKey, Reference = reference,
            CommercialModel = CommercialModel.FixedPrice, TotalContractValue = 1000m, Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = ContractStatus.Active,
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        return (contract.ContractKey, reference);
    }
}
