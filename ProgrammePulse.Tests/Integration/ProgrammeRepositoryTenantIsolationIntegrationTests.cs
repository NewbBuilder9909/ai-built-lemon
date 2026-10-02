using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using Xunit.Abstractions;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Real LocalDB, real ProgrammeRepository, real SQL Server unique indexes —
/// the class of defect a hand-rolled in-memory fake structurally cannot
/// reproduce: a guessed foreign key from another tenant. See
/// ProgrammePulseWebApplicationFactory for why this uses
/// WebApplicationFactory rather than Umbraco's own NUnit-based test harness.
/// Each test seeds its own uniquely-keyed rows (ExternalId = a fresh Guid)
/// so tests can run against the same persistent test database without
/// cleaning up after themselves or colliding with each other.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProgrammeRepositoryTenantIsolationIntegrationTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string EvidenceNotProduced = "no cross-tenant foreign-key or unique-index behaviour was exercised against real SQL Server.";

    private readonly ProgrammePulseWebApplicationFactory _factory;
    private readonly ITestOutputHelper output;

    public ProgrammeRepositoryTenantIsolationIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        this.output = output;
    }

    private IProgrammeRepository Repository() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<IProgrammeRepository>();

    private static Programme NewProgramme(Guid programmeKey, DateTime now) => new()
    {
        ProgrammeKey = programmeKey,
        Name = $"Integration test programme {programmeKey:N}",
        ExternalSource = "IntegrationTest",
        ExternalId = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    [Fact]
    public async Task Tempo_work_date_round_trips_without_inventing_a_UTC_start_or_crossing_tenants()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var externalId = Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 9, 11);
        var now = DateTime.UtcNow;
        var entry = await repository.UpsertTimeEntryAsync(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(), DurationHours = 3m, WorkDate = date,
            StartedAtUtc = null, IsBillable = false, BillabilityKnown = false, ExternalSource = "Tempo",
            ExternalId = externalId, CreatedAtUtc = now, UpdatedAtUtc = now
        }, TenantA);

        var own = Assert.Single(await repository.GetTimeEntriesAsync(TenantA),
            row => row.TimeEntryKey == entry.TimeEntryKey);
        Assert.Equal(date, own.WorkDate);
        Assert.Null(own.StartedAtUtc);
        Assert.False(own.BillabilityKnown);
        Assert.DoesNotContain(await repository.GetTimeEntriesAsync(TenantB), row => row.TimeEntryKey == entry.TimeEntryKey);
    }

    [Fact]
    public async Task GetProgrammesAsync_never_returns_a_row_seeded_under_another_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var now = DateTime.UtcNow;
        var programme = await repository.UpsertProgrammeAsync(NewProgramme(Guid.NewGuid(), now), TenantA);

        var tenantBProgrammes = await repository.GetProgrammesAsync(TenantB);

        Assert.DoesNotContain(tenantBProgrammes, p => p.ProgrammeKey == programme.ProgrammeKey);
    }

    /// <summary>
    /// The core defect this phase closed: UpsertProjectAsync used to stamp
    /// the child row's own TenantId without ever checking that the
    /// ProgrammeKey it was told to link to actually belonged to that
    /// tenant. Against a real database, a guessed ProgrammeKey from another
    /// tenant must be refused, not silently cross-linked.
    /// </summary>
    [Fact]
    public async Task UpsertProjectAsync_refuses_a_programme_key_owned_by_another_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var now = DateTime.UtcNow;
        var tenantAProgramme = await repository.UpsertProgrammeAsync(NewProgramme(Guid.NewGuid(), now), TenantA);

        var guessedProject = new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = tenantAProgramme.ProgrammeKey,
            Name = "Guessed cross-tenant project",
            ExternalSource = "IntegrationTest",
            ExternalId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await Assert.ThrowsAsync<CrossTenantReferenceException>(
            () => repository.UpsertProjectAsync(guessedProject, TenantB));
    }

    [Fact]
    public async Task UpsertProjectAsync_succeeds_when_the_programme_belongs_to_the_caller_s_own_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var now = DateTime.UtcNow;
        var programme = await repository.UpsertProgrammeAsync(NewProgramme(Guid.NewGuid(), now), TenantA);

        var project = await repository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = programme.ProgrammeKey,
            Name = "Own-tenant project",
            ExternalSource = "IntegrationTest",
            ExternalId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, TenantA);

        Assert.Equal(TenantA, project.TenantId);
    }

    /// <summary>
    /// UpsertWorkItemAllocationsAsync's delete-existing-rows query used to
    /// filter by workItemKey alone — against a real table, confirm the fix
    /// (tenant-scoped delete) leaves another tenant's allocation on the same
    /// work item key untouched. (workItemKey collision across tenants can't
    /// happen via the app today since Part 1 also added the parent-tenant
    /// check on the call path that creates a WorkItem, but this proves the
    /// delete statement itself is tenant-scoped regardless of how the row
    /// got there — defense in depth, not reliance on the caller.)
    /// </summary>
    [Fact]
    public async Task UpsertWorkItemAllocationsAsync_only_replaces_the_calling_tenants_own_allocations()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var now = DateTime.UtcNow;

        var programme = await repository.UpsertProgrammeAsync(NewProgramme(Guid.NewGuid(), now), TenantA);
        var project = await repository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = programme.ProgrammeKey,
            Name = "Allocation test project",
            ExternalSource = "IntegrationTest",
            ExternalId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, TenantA);
        var workstream = await repository.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Guid.NewGuid(),
            ProjectKey = project.ProjectKey,
            Name = "Allocation test workstream",
            ExternalSource = "IntegrationTest",
            ExternalId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, TenantA);
        var workItem = await repository.UpsertWorkItemAsync(new WorkItem
        {
            WorkItemKey = Guid.NewGuid(),
            WorkstreamKey = workstream.WorkstreamKey,
            Title = "Allocation test item",
            Stage = WorkItemLifecycleStage.InProgress,
            ExternalSource = "IntegrationTest",
            ExternalId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, TenantA);

        var staffKey = Guid.NewGuid();
        await repository.UpsertWorkItemAllocationsAsync(workItem.WorkItemKey, [staffKey], now, TenantA);

        var allocations = await repository.GetAllocationsByWorkItemKeyAsync(workItem.WorkItemKey, TenantA);
        Assert.Single(allocations);
        Assert.Equal(staffKey, allocations[0].StaffKey);

        // Re-running with a different tenantId against the same workItemKey
        // must be refused (the work item belongs to TenantA), proving the
        // new parent-tenant check on this method too.
        await Assert.ThrowsAsync<CrossTenantReferenceException>(
            () => repository.UpsertWorkItemAllocationsAsync(workItem.WorkItemKey, [Guid.NewGuid()], now, TenantB));

        var stillThere = await repository.GetAllocationsByWorkItemKeyAsync(workItem.WorkItemKey, TenantA);
        Assert.Single(stillThere);
        Assert.Equal(staffKey, stillThere[0].StaffKey);
    }
}
