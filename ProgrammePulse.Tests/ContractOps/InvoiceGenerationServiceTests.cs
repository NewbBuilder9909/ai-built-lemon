using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Tests.ProgrammeOps;

namespace ProgrammePulse.Tests.ContractOps;

/// <summary>
/// Invoices after decision 4 of docs/delivery-evidence-and-contract-assurance.md:
/// generated as drafts, dated by <see cref="TimeEntry.ReportDate"/> (so Tempo
/// time is invoiced), billing only known-billable hours and listing the rest
/// for review, and numbered only when issued.
/// </summary>
public class InvoiceGenerationServiceTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly FakeProgrammeRepository _programmes = new();
    private readonly FakeContractRepository _contracts = new();
    private readonly RecordingContractAudit _audit = new();

    private InvoiceGenerationService Sut() => new(_contracts, _programmes, new FixedTimeProvider(Now));

    // Only the invoice members are exercised; the other collaborators are not reached.
    private ContractAdminService Admin() => new(_contracts, _programmes, null!, null!, Sut(), _audit, new FixedTimeProvider(Now));

    // ---- Generation ----

    [Fact]
    public async Task Generates_a_draft_with_no_number_billing_period_hours_at_the_contract_rate()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-01");
        AddEntry(workItem, 8m, started: new DateTime(2026, 6, 5));
        AddEntry(workItem, 100m, started: new DateTime(2026, 5, 1)); // outside the period

        var invoice = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.Equal((InvoiceStatus.Draft, false, (DateTime?)null), (invoice.Status, invoice.HasNumber, invoice.IssuedAtUtc));
        Assert.StartsWith(Invoice.DraftNumberPrefix, invoice.InvoiceNumber);
        Assert.Equal(800m, invoice.Subtotal);
        var line = Assert.Single(_contracts.InvoiceLines);
        Assert.Equal((8m, 100m), (line.Quantity!.Value, line.UnitRate!.Value));
    }

    [Fact]
    public async Task An_entry_is_invoiced_in_the_period_of_its_work_date_so_Tempo_time_is_billed()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-02");
        AddEntry(workItem, 3m, workDate: new DateOnly(2026, 6, 10)); // Tempo: work date only
        // Work date wins over a UTC start on another day (a browser-time-zone stamp).
        AddEntry(workItem, 2m, started: new DateTime(2026, 5, 31, 23, 30, 0, DateTimeKind.Utc), workDate: new DateOnly(2026, 6, 1));
        AddEntry(workItem, 5m, started: new DateTime(2026, 6, 30, 23, 30, 0, DateTimeKind.Utc), workDate: new DateOnly(2026, 7, 1));

        var invoice = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.Equal(5m, Assert.Single(_contracts.InvoiceLines).Quantity);
        Assert.Equal(500m, invoice.Subtotal);
    }

    [Fact]
    public async Task Unknown_billability_is_listed_for_review_never_billed_and_known_non_billable_is_left_out()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-03");
        AddEntry(workItem, 4m, started: new DateTime(2026, 6, 2));
        AddEntry(workItem, 6m, started: new DateTime(2026, 6, 3), billabilityKnown: false);
        AddEntry(workItem, 9m, started: new DateTime(2026, 6, 4), billable: false);

        var invoice = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.Equal((4m, 6m), (Assert.Single(_contracts.InvoiceLines).Quantity!.Value, invoice.NeedsReviewHours));
    }

    [Fact]
    public async Task Hours_are_billed_whether_or_not_the_person_has_a_cost_rate()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-04");
        AddEntry(workItem, 7m, started: new DateTime(2026, 6, 5), staffKey: Guid.NewGuid()); // no StaffRate anywhere

        var invoice = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.Equal(700m, invoice.Subtotal);
    }

    [Fact]
    public async Task Only_unconfirmed_hours_is_refused_with_the_hours_named_and_nothing_at_all_is_refused()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-05");

        var nothing = await Assert.ThrowsAsync<InvoiceGenerationException>(() =>
            Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId));
        Assert.Contains("Nothing to invoice", nothing.Message);

        AddEntry(workItem, 6m, started: new DateTime(2026, 6, 3), billabilityKnown: false);
        var unconfirmed = await Assert.ThrowsAsync<InvoiceGenerationException>(() =>
            Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId));
        Assert.Contains("6 hours have no confirmed billability", unconfirmed.Message);
        Assert.Empty(_contracts.Invoices);
    }

    [Fact]
    public async Task Non_labour_costs_in_the_period_are_invoiced_for_any_commercial_model()
    {
        var customer = SeedCustomerWithWorkItem().Customer;
        var contract = AddContract("FP-01", customer, CommercialModel.FixedPrice, billRate: null);
        AddCost(contract, "Equipment rental", 750m, new DateOnly(2026, 6, 10));
        AddCost(contract, "Later expense", 999m, new DateOnly(2026, 8, 1));

        var invoice = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.Equal(750m, invoice.Subtotal);
        var line = Assert.Single(_contracts.InvoiceLines);
        Assert.Equal(("Equipment rental", (decimal?)null), (line.Description, line.Quantity));
    }

    // ---- Lifecycle ----

    [Theory]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Issued, true)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Discarded, true)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Paid, true)]
    [InlineData(InvoiceStatus.Draft, InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceStatus.Issued, InvoiceStatus.Discarded, false)]
    [InlineData(InvoiceStatus.Discarded, InvoiceStatus.Issued, false)]
    [InlineData(InvoiceStatus.Paid, InvoiceStatus.Issued, false)]
    public void Only_forward_moves_are_allowed(InvoiceStatus from, InvoiceStatus to, bool allowed)
    {
        Assert.Equal(allowed, InvoiceLifecycle.CanMove(from, to));
    }

    [Fact]
    public async Task Numbers_are_assigned_at_issue_so_a_discarded_draft_leaves_no_gap()
    {
        var customer = SeedCustomerWithWorkItem().Customer;
        var contract = AddContract("SEQ", customer, CommercialModel.FixedPrice, billRate: null);
        AddCost(contract, "A", 100m, new DateOnly(2026, 1, 15));
        AddCost(contract, "B", 100m, new DateOnly(2026, 2, 15));

        var discarded = await Sut().GenerateAsync(contract.ContractKey, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, TestTenantId);
        var first = await Sut().GenerateAsync(contract.ContractKey, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, TestTenantId);
        var second = await Sut().GenerateAsync(contract.ContractKey, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), null, TestTenantId);

        Assert.True((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, discarded.InvoiceKey, InvoiceStatus.Discarded, false, 1)).Succeeded);
        Assert.True((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, second.InvoiceKey, InvoiceStatus.Issued, false, 1)).Succeeded);
        Assert.True((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, first.InvoiceKey, InvoiceStatus.Issued, false, 1)).Succeeded);

        Assert.Equal(("SEQ-001", Now), (Stored(second).InvoiceNumber, Stored(second).IssuedAtUtc!.Value));
        Assert.Equal("SEQ-002", Stored(first).InvoiceNumber);
        Assert.False(Stored(discarded).HasNumber);
        Assert.Contains(_audit.Entries, e => e.Action == "Issued" && e.DetailJson!.Contains("SEQ-001"));
    }

    [Fact]
    public async Task Issuing_with_unconfirmed_hours_needs_the_reviewer_to_confirm_them()
    {
        var (contract, workItem) = SeedTimeAndMaterials("TM-06");
        AddEntry(workItem, 4m, started: new DateTime(2026, 6, 2));
        AddEntry(workItem, 6m, started: new DateTime(2026, 6, 3), billabilityKnown: false);
        var draft = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        var refused = await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Issued, false, 1);
        Assert.False(refused.Succeeded);
        Assert.Contains("6 hours", refused.Error);
        Assert.Equal(InvoiceStatus.Draft, Stored(draft).Status);

        var issued = await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Issued, true, 1);
        Assert.True(issued.Succeeded);
        Assert.Contains(_audit.Entries, e => e.Action == "Issued" && e.DetailJson!.Contains("\"needsReviewHoursConfirmed\":true"));
    }

    [Fact]
    public async Task A_draft_cannot_be_marked_paid_and_a_paid_invoice_cannot_move_back()
    {
        var customer = SeedCustomerWithWorkItem().Customer;
        var contract = AddContract("LC", customer, CommercialModel.FixedPrice, billRate: null);
        AddCost(contract, "A", 100m, new DateOnly(2026, 6, 15));
        var draft = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        Assert.False((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Paid, false, 1)).Succeeded);

        await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Issued, false, 1);
        Assert.True((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Paid, false, 1)).Succeeded);
        Assert.False((await Admin().ChangeInvoiceStatusAsync(TestTenantId, contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Issued, false, 1)).Succeeded);
        Assert.Equal(InvoiceStatus.Paid, Stored(draft).Status);
    }

    [Fact]
    public async Task Another_tenants_invoice_is_not_found()
    {
        var customer = SeedCustomerWithWorkItem().Customer;
        var contract = AddContract("X", customer, CommercialModel.FixedPrice, billRate: null);
        AddCost(contract, "A", 100m, new DateOnly(2026, 6, 15));
        var draft = await Sut().GenerateAsync(contract.ContractKey, June.Start, June.End, null, TestTenantId);

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() =>
            Admin().ChangeInvoiceStatusAsync(Guid.NewGuid(), contract.ContractKey, draft.InvoiceKey, InvoiceStatus.Issued, false, 1));
    }

    // ---- Helpers ----

    private static class June
    {
        public static readonly DateOnly Start = new(2026, 6, 1);
        public static readonly DateOnly End = new(2026, 6, 30);
    }

    private Invoice Stored(Invoice invoice) => _contracts.Invoices.Single(i => i.InvoiceKey == invoice.InvoiceKey);

    private (Contract Contract, Guid WorkItemKey) SeedTimeAndMaterials(string reference)
    {
        var (customer, workItem) = SeedCustomerWithWorkItem();
        return (AddContract(reference, customer, CommercialModel.TimeAndMaterials, billRate: 100m), workItem);
    }

    private Contract AddContract(string reference, Customer customer, CommercialModel model, decimal? billRate)
    {
        var contract = new Contract
        {
            ContractKey = Guid.NewGuid(),
            TenantId = TestTenantId,
            CustomerKey = customer.CustomerKey,
            Reference = reference,
            CommercialModel = model,
            BillRate = billRate,
            TotalContractValue = model == CommercialModel.FixedPrice ? 10_000m : null,
            Currency = "GBP",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.Active,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };
        _contracts.Contracts.Add(contract);
        return contract;
    }

    private void AddCost(Contract contract, string description, decimal amount, DateOnly incurredOn) =>
        _contracts.NonLabourCosts.Add(new NonLabourCost
        {
            NonLabourCostKey = Guid.NewGuid(), TenantId = TestTenantId, ContractKey = contract.ContractKey, Description = description,
            Amount = amount, Currency = "GBP", IncurredOn = incurredOn, CreatedAtUtc = Now
        });

    private void AddEntry(Guid workItem, decimal hours, DateTime? started = null, DateOnly? workDate = null,
        bool billable = true, bool billabilityKnown = true, Guid? staffKey = null) =>
        _programmes.TimeEntries.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(), TenantId = TestTenantId, WorkItemKey = workItem, StaffKey = staffKey ?? Guid.NewGuid(),
            DurationHours = hours, StartedAtUtc = started, WorkDate = workDate, IsBillable = billable, BillabilityKnown = billabilityKnown,
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        });

    private (Customer Customer, Guid WorkItemKey) SeedCustomerWithWorkItem()
    {
        var customer = new Customer { CustomerKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Acme Broadcasting", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        _programmes.Customers.Add(customer);

        var programme = new Programme { ProgrammeKey = Guid.NewGuid(), TenantId = TestTenantId, Name = "Drama", CustomerKey = customer.CustomerKey, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var project = new Project { ProjectKey = Guid.NewGuid(), TenantId = TestTenantId, ProgrammeKey = programme.ProgrammeKey, Name = "Series 4", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workstream = new Workstream { WorkstreamKey = Guid.NewGuid(), TenantId = TestTenantId, ProjectKey = project.ProjectKey, Name = "Post", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        var workItem = new WorkItem { WorkItemKey = Guid.NewGuid(), TenantId = TestTenantId, WorkstreamKey = workstream.WorkstreamKey, Title = "Edit", Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = Now, UpdatedAtUtc = Now };

        _programmes.Programmes.Add(programme);
        _programmes.Projects.Add(project);
        _programmes.Workstreams.Add(workstream);
        _programmes.WorkItems.Add(workItem);

        return (customer, workItem.WorkItemKey);
    }
}
