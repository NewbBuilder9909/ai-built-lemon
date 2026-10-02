using ProgrammePulse.Tests.Personas;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.ContractOps;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ContractOps;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Renders the Contracts Overview page over real HTTP as a signed-in tenant
/// administrator, against a book seeded to contain the exact situation the
/// page has to report honestly: two overlapping contracts for one customer
/// that select the same logged hours.
///
/// This exists because `ProgrammePulse.csproj` sets
/// `RazorCompileOnBuild=false` (required by Umbraco's InMemoryAuto models
/// mode), so a `.cshtml` is compiled on first request, not at build time. A
/// green `dotnet build` therefore proves nothing about a view — a mistake in
/// Overview.cshtml would first surface as a 500 in front of whoever opened
/// the page, and the unit tests above would still be green. Asking for the
/// page, with rows in it, is the only evidence that it renders.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ContractsOverviewRenderIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the Contracts Overview page was never compiled or rendered.";

    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_portfolio_overview_renders_and_never_shows_a_blended_margin()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var admin = NorthstarPersonas.TenantAdmin;
        await new NorthstarPersonaSeeder(factory.Services).SeedWithEveryModuleOnAsync([admin]);
        await SeedBookAsync(admin);

        var session = await PersonaSignIn.SignInAsync(factory, admin);

        using var response = await session.GetAsync("/staffops/contracts/overview");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(AccessOutcome.Allowed, PersonaSession.Outcome(response));

        // The correction this page carries: a reader must not be able to take
        // away a single blended portfolio margin, and every figure names what
        // it measures.
        Assert.Contains("no single portfolio margin", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Totals by currency and basis", body, StringComparison.Ordinal);
        Assert.Contains("headroom to date", body, StringComparison.Ordinal);
        Assert.Contains("chargeable less cost", body, StringComparison.Ordinal);

        // The clean fixed-price contract (10,000 value, 1,000 cost) and the
        // clean T&M contract (2,000 chargeable, 500 cost) are two different
        // measures. Their blended margin, 10,500, must appear nowhere.
        Assert.DoesNotContain("10,500", body, StringComparison.Ordinal);
        Assert.Contains("9,000", body, StringComparison.Ordinal);   // headroom row
        Assert.Contains("1,500", body, StringComparison.Ordinal);   // chargeable-less-cost row

        // Two overlapping contracts, 40 shared hours at 50/hr. Held out once
        // (2,000 + the 500 each selects alone), not twice.
        Assert.Contains("2 contract(s) excluded", body, StringComparison.Ordinal);
        Assert.Contains("3,000 GBP across 2 contract(s)", body, StringComparison.Ordinal);
        Assert.Contains("2,000 is claimed by more than one contract", body, StringComparison.Ordinal);

        // Disputed cost and the overstatement a naive sum would produce are
        // separate sentences, because they are separate quantities — they
        // happen to coincide at two contracts and diverge at three.
        Assert.Contains("the cost under dispute, counted once", body, StringComparison.Ordinal);
        Assert.Contains("would have overstated the total above by", body, StringComparison.Ordinal);

        // The word this page deliberately does not use for contracted or
        // chargeable value.
        Assert.DoesNotContain(">Revenue<", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A customer with one work item, hours logged across three periods, and
    /// four contracts: one clean fixed price, one clean Time &amp; Materials,
    /// and two whose terms overlap.
    /// </summary>
    private async Task SeedBookAsync(PersonaDefinition admin)
    {
        using var scope = factory.Services.CreateScope();
        var programmes = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var rates = scope.ServiceProvider.GetRequiredService<IStaffRateRepository>();
        var tenantId = admin.TenantKey;

        await rates.SetCurrentRateAsync(admin.StaffKey, 50m, "GBP", admin.StaffKey);

        // Every key below is fixed, and contracts are created only if their
        // reference is absent. The integration database is shared across the
        // whole run and is not dropped between runs, so a seed that inserted
        // a fresh copy each time would eventually make every contract overlap
        // another one and the assertions would drift.
        var customer = await programmes.UpsertCustomerAsync(new Customer
        {
            CustomerKey = Key(0x01),
            TenantId = tenantId,
            Name = "Overview Render Customer",
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

        var programme = await programmes.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Key(0x02),
            TenantId = tenantId,
            Name = "Overview Render Programme",
            CustomerKey = customer.CustomerKey,
            ExternalSource = "RenderTest",
            ExternalId = "overview-programme",
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

        var project = await programmes.UpsertProjectAsync(new Project
        {
            ProjectKey = Key(0x03),
            TenantId = tenantId,
            ProgrammeKey = programme.ProgrammeKey,
            Name = "Overview Render Project",
            ExternalSource = "RenderTest",
            ExternalId = "overview-project",
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

        var workstream = await programmes.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Key(0x04),
            TenantId = tenantId,
            ProjectKey = project.ProjectKey,
            Name = "Overview Render Workstream",
            ExternalSource = "RenderTest",
            ExternalId = "overview-workstream",
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

        var workItem = await programmes.UpsertWorkItemAsync(new WorkItem
        {
            WorkItemKey = Key(0x05),
            TenantId = tenantId,
            WorkstreamKey = workstream.WorkstreamKey,
            Title = "Overview Render Work Item",
            Stage = WorkItemLifecycleStage.InProgress,
            ExternalSource = "RenderTest",
            ExternalId = "overview-workitem",
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

        // Hours in five periods. The two clean contracts hold 2030 and 2031,
        // which no other term reaches, so their cost is theirs alone. The
        // overlapping pair splits 2032/2033, and the 40 hours in 2032-09 fall
        // inside both terms.
        await LogHoursAsync(programmes, workItem.WorkItemKey, admin.StaffKey, tenantId, 20m, new DateTime(2030, 3, 1), 0x10, "clean-fixed");
        await LogHoursAsync(programmes, workItem.WorkItemKey, admin.StaffKey, tenantId, 10m, new DateTime(2031, 3, 1), 0x11, "clean-tm");
        await LogHoursAsync(programmes, workItem.WorkItemKey, admin.StaffKey, tenantId, 10m, new DateTime(2032, 3, 1), 0x12, "overlap-a-only");
        await LogHoursAsync(programmes, workItem.WorkItemKey, admin.StaffKey, tenantId, 40m, new DateTime(2032, 9, 1), 0x13, "overlap-shared");
        await LogHoursAsync(programmes, workItem.WorkItemKey, admin.StaffKey, tenantId, 10m, new DateTime(2033, 3, 1), 0x14, "overlap-b-only");

        var existing = (await contracts.GetContractsAsync(tenantId)).Select(c => c.Reference).ToHashSet(StringComparer.Ordinal);

        // Clean fixed price: 10,000 value less 1,000 cost = 9,000 headroom.
        await EnsureContractAsync(contracts, existing, "Render Clean FP", customer.CustomerKey, tenantId,
            CommercialModel.FixedPrice, new DateOnly(2030, 1, 1), new DateOnly(2030, 12, 31), totalContractValue: 10_000m);

        // Clean T&M: 10 billable hours x 200 = 2,000 chargeable, less 500 cost = 1,500.
        await EnsureContractAsync(contracts, existing, "Render Clean TM", customer.CustomerKey, tenantId,
            CommercialModel.TimeAndMaterials, new DateOnly(2031, 1, 1), new DateOnly(2031, 12, 31), billRate: 200m);

        // Overlapping pair: A holds 2032, B holds 2032-09 onward. The 40 hours
        // in 2032-09 are selected by both.
        await EnsureContractAsync(contracts, existing, "Render Overlap A", customer.CustomerKey, tenantId,
            CommercialModel.FixedPrice, new DateOnly(2032, 1, 1), new DateOnly(2032, 12, 31), totalContractValue: 50_000m);
        await EnsureContractAsync(contracts, existing, "Render Overlap B", customer.CustomerKey, tenantId,
            CommercialModel.FixedPrice, new DateOnly(2032, 9, 1), new DateOnly(2033, 8, 31), totalContractValue: 50_000m);
    }

    /// <summary>A stable key per seeded row, so re-running the suite reuses rather than duplicates.</summary>
    private static Guid Key(byte ordinal) => new([0xc0, 0x11, 0xec, 0x7e, 0xd0, 0x05, 0x40, 0x00, 0x80, 0x00, 0, 0, 0, 0, 0, ordinal]);

    private static Task EnsureContractAsync(
        IContractRepository contracts, HashSet<string> existing, string reference, Guid customerKey, Guid tenantId,
        CommercialModel model, DateOnly start, DateOnly end, decimal? totalContractValue = null, decimal? billRate = null) =>
        existing.Contains(reference)
            ? Task.CompletedTask
            : contracts.CreateContractAsync(Contract(reference, customerKey, tenantId, model, start, end, totalContractValue, billRate), tenantId);

    private static Task LogHoursAsync(
        IProgrammeRepository programmes, Guid workItemKey, Guid staffKey, Guid tenantId,
        decimal hours, DateTime startedAtUtc, byte ordinal, string externalId) =>
        programmes.UpsertTimeEntryAsync(new TimeEntry
        {
            TimeEntryKey = Key(ordinal),
            TenantId = tenantId,
            WorkItemKey = workItemKey,
            StaffKey = staffKey,
            DurationHours = hours,
            StartedAtUtc = startedAtUtc,
            IsBillable = true,
            ExternalSource = "RenderTest",
            ExternalId = externalId,
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        }, tenantId);

    private static Contract Contract(
        string reference, Guid customerKey, Guid tenantId, CommercialModel model,
        DateOnly start, DateOnly end, decimal? totalContractValue = null, decimal? billRate = null) => new()
        {
            ContractKey = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerKey = customerKey,
            Reference = reference,
            CommercialModel = model,
            TotalContractValue = totalContractValue,
            BillRate = billRate,
            Currency = "GBP",
            StartDate = start,
            EndDate = end,
            Status = ContractStatus.Active,
            CreatedAtUtc = SeededAt,
            UpdatedAtUtc = SeededAt
        };
}
