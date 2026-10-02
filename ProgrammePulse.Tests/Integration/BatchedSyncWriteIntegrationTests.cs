using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Finding A3: sync used to write each task and time entry with several
/// round trips. The batched upserts replace that with set-based statements,
/// and must write exactly what the single-row path writes. These are
/// differential tests: the same sequence of upserts goes through the
/// single-row path in one tenant and the batched path in another, then every
/// persisted field is compared. The sequence deliberately includes a
/// re-upsert, a duplicate inside one batch, a row with no external id, null
/// and decimal values, an entry dated only by its UTC start, and enough rows
/// to cross the statement chunk boundaries.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BatchedSyncWriteIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the batched sync writes were not compared with the single-row writes.";
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private IProgrammeRepository Repository() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<IProgrammeRepository>();

    [Fact]
    public async Task Batched_work_item_upserts_write_exactly_what_single_row_upserts_write()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var (singleTenant, singleWorkstream) = (Guid.NewGuid(), Guid.Empty);
        var (batchTenant, batchWorkstream) = (Guid.NewGuid(), Guid.Empty);
        singleWorkstream = await SeedWorkstreamAsync(repository, singleTenant);
        batchWorkstream = await SeedWorkstreamAsync(repository, batchTenant);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        // Round 1 creates; round 2 re-upserts some, adds more, and repeats one
        // external id inside the batch (last one wins, as two single calls would).
        IReadOnlyList<(string? Id, string Title, WorkItemLifecycleStage Stage, decimal? Hours, Guid[] People)> round1 =
        [
            ("task-a", "A", WorkItemLifecycleStage.Backlog, 2.5m, [alice, bob]),
            ("task-b", "B", WorkItemLifecycleStage.InProgress, null, [bob]),
            (null, "No external id", WorkItemLifecycleStage.Ready, 1m, []),
        ];
        var bulk = Enumerable.Range(0, 300).Select(i => ((string?)$"bulk-{i}", $"Bulk {i}", WorkItemLifecycleStage.Done, (decimal?)(i % 3 == 0 ? null : i / 4m), i % 2 == 0 ? new[] { alice } : Array.Empty<Guid>()));
        IReadOnlyList<(string? Id, string Title, WorkItemLifecycleStage Stage, decimal? Hours, Guid[] People)> round2 =
        [
            ("task-a", "A renamed", WorkItemLifecycleStage.Blocked, 3.25m, [bob]),
            ("task-c", "C first", WorkItemLifecycleStage.Ready, 1m, [alice]),
            ("task-c", "C second", WorkItemLifecycleStage.InReview, 4m, [bob, alice]),
            .. bulk,
        ];

        foreach (var round in new[] { round1, round2 })
        {
            foreach (var row in round)
            {
                var saved = await repository.UpsertWorkItemAsync(NewWorkItem(row, singleWorkstream), singleTenant);
                await repository.UpsertWorkItemAllocationsAsync(saved.WorkItemKey, row.People, Now, singleTenant);
            }

            var returned = await repository.UpsertWorkItemsAsync(
                round.Select(row => new WorkItemUpsert(NewWorkItem(row, batchWorkstream), row.People)).ToList(), Now, batchTenant);
            Assert.Equal(round.Select(r => r.Title), returned.Select(r => r.Title));
        }

        Assert.Equal(await SnapshotWorkItemsAsync(repository, singleTenant), await SnapshotWorkItemsAsync(repository, batchTenant));
        // 3 in round 1, then task-c and 300 bulk rows new in round 2 (task-a re-upserted).
        Assert.Equal(304, (await repository.GetWorkItemsAsync(batchTenant)).Count);
    }

    [Fact]
    public async Task Batched_time_entry_upserts_write_exactly_what_single_row_upserts_write()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var singleTenant = Guid.NewGuid();
        var batchTenant = Guid.NewGuid();
        var singleItem = await SeedWorkItemAsync(repository, singleTenant);
        var batchItem = await SeedWorkItemAsync(repository, batchTenant);
        var person = Guid.NewGuid();

        IReadOnlyList<(string? Id, bool Linked, decimal Hours, DateOnly? WorkDate, DateTime? Started, bool BillabilityKnown)> round1 =
        [
            ("te-1", true, 1.5m, new DateOnly(2026, 3, 10), null, true),
            ("te-2", false, 0.25m, null, new DateTime(2026, 3, 31, 23, 30, 0, DateTimeKind.Utc), true),
            ("te-3", true, 2m, null, null, false),
            (null, true, 7m, new DateOnly(2026, 4, 1), null, true),
        ];
        IReadOnlyList<(string? Id, bool Linked, decimal Hours, DateOnly? WorkDate, DateTime? Started, bool BillabilityKnown)> round2 =
        [
            ("te-1", false, 1.75m, new DateOnly(2026, 3, 11), null, false),
            ("te-4", true, 3m, new DateOnly(2026, 3, 12), null, true),
            ("te-4", true, 3.5m, new DateOnly(2026, 3, 13), null, true),
            .. Enumerable.Range(0, 250).Select(i => ((string?)$"te-bulk-{i}", i % 2 == 0, i / 8m, (DateOnly?)new DateOnly(2026, 3, 1).AddDays(i % 28), (DateTime?)null, true)),
        ];

        foreach (var round in new[] { round1, round2 })
        {
            foreach (var row in round)
            {
                await repository.UpsertTimeEntryAsync(NewTimeEntry(row, singleItem, person), singleTenant);
            }

            await repository.UpsertTimeEntriesAsync(round.Select(row => NewTimeEntry(row, batchItem, person)).ToList(), batchTenant);
        }

        Assert.Equal(await SnapshotTimeEntriesAsync(repository, singleTenant), await SnapshotTimeEntriesAsync(repository, batchTenant));
        // 4 in round 1, then te-4 and 250 bulk rows new in round 2 (te-1 re-upserted).
        Assert.Equal(255, (await repository.GetTimeEntriesAsync(batchTenant)).Count);
    }

    [Fact]
    public async Task A_batch_naming_another_tenants_parent_fails_whole_and_writes_nothing()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Repository();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var ownWorkstream = await SeedWorkstreamAsync(repository, tenant);
        var foreignWorkstream = await SeedWorkstreamAsync(repository, otherTenant);

        var exception = await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.UpsertWorkItemsAsync(
        [
            new WorkItemUpsert(NewWorkItem(("ok-1", "Fine", WorkItemLifecycleStage.Ready, null, []), ownWorkstream), []),
            new WorkItemUpsert(NewWorkItem(("bad-1", "Foreign parent", WorkItemLifecycleStage.Ready, null, []), foreignWorkstream), []),
        ], Now, tenant));

        Assert.Contains(foreignWorkstream.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await repository.GetWorkItemsAsync(tenant));

        var foreignItem = await SeedWorkItemAsync(repository, otherTenant);
        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => repository.UpsertTimeEntriesAsync(
            [NewTimeEntry(("te-foreign", true, 1m, new DateOnly(2026, 3, 1), null, true), foreignItem, Guid.NewGuid())], tenant));
        Assert.Empty(await repository.GetTimeEntriesAsync(tenant));
    }

    [Fact]
    public async Task Batched_raw_capture_stores_every_payload_verbatim()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var tenant = Guid.NewGuid();
        var payloads = Enumerable.Range(0, 450).Select(i => ($"raw-{i}", $$"""{"id":"raw-{{i}}","name":"O'Brien \"quoted\" {{i}}"}""")).ToList();
        var raw = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IClickUpRawPayloadRepository>();

        await raw.SaveManyAsync("task", payloads, "workspace-1", Now, tenant);

        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var stored = await scope.Database.FetchAsync<string>(
            "SELECT payloadJson FROM ProgrammeOps_RawClickUpPayload WHERE tenantId = @0 AND entityType = @1", tenant, "task");
        Assert.Equal(payloads.Select(p => p.Item2).Order(), stored.Order());
    }

    private static WorkItem NewWorkItem((string? Id, string Title, WorkItemLifecycleStage Stage, decimal? Hours, Guid[] People) row, Guid workstream) => new()
    {
        WorkItemKey = Guid.NewGuid(),
        WorkstreamKey = workstream,
        Title = row.Title,
        Stage = row.Stage,
        RawStatus = row.Stage.ToString().ToLowerInvariant(),
        IsMilestone = row.Title.StartsWith('A'),
        AssignedStaffKey = row.People.Length > 0 ? row.People[0] : null,
        DueDateUtc = row.Hours is null ? null : Now.AddDays(7),
        EstimatedHours = row.Hours,
        ExternalSource = row.Id is null ? null : "IntegrationTest",
        ExternalId = row.Id,
        ParentExternalId = row.Id == "task-b" ? "task-a" : null,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    private static TimeEntry NewTimeEntry((string? Id, bool Linked, decimal Hours, DateOnly? WorkDate, DateTime? Started, bool BillabilityKnown) row, Guid workItem, Guid person) => new()
    {
        TimeEntryKey = Guid.NewGuid(),
        WorkItemKey = row.Linked ? workItem : null,
        StaffKey = row.Id == "te-3" ? null : person,
        DurationHours = row.Hours,
        WorkDate = row.WorkDate,
        StartedAtUtc = row.Started,
        IsBillable = row.Hours > 1m,
        BillabilityKnown = row.BillabilityKnown,
        ExternalSource = row.Id is null ? null : "IntegrationTest",
        ExternalId = row.Id,
        // Unlinked rows still name their source item, which both write paths must keep.
        SourceWorkItemExternalId = row.Id is null ? null : "item-of-" + row.Id,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
    };

    // Everything persisted except the generated keys and tenant, ordered by
    // what identifies a row across the two tenants.
    private static async Task<List<string>> SnapshotWorkItemsAsync(IProgrammeRepository repository, Guid tenant)
    {
        var items = await repository.GetWorkItemsAsync(tenant);
        var allocations = (await repository.GetAllocationsAsync(tenant)).ToLookup(a => a.WorkItemKey);
        return items
            .Select(i => string.Join("|", i.ExternalId ?? "(none)", i.Title, i.Stage, i.RawStatus, i.IsMilestone, i.AssignedStaffKey,
                i.DueDateUtc?.ToString("O"), i.EstimatedHours, i.ExternalSource, i.ParentExternalId, i.CreatedAtUtc.ToString("O"), i.UpdatedAtUtc.ToString("O"),
                string.Join(",", allocations[i.WorkItemKey].OrderByDescending(a => a.IsPrimary).ThenBy(a => a.StaffKey).Select(a => $"{a.StaffKey}:{a.IsPrimary}"))))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<string>> SnapshotTimeEntriesAsync(IProgrammeRepository repository, Guid tenant) =>
        (await repository.GetTimeEntriesAsync(tenant))
            .Select(t => string.Join("|", t.ExternalId ?? "(none)", t.WorkItemKey is not null, t.StaffKey, t.DurationHours, t.WorkDate,
                t.StartedAtUtc?.ToString("O"), t.IsBillable, t.BillabilityKnown, t.ExternalSource, t.SourceWorkItemExternalId ?? "(none)", t.CreatedAtUtc.ToString("O"), t.UpdatedAtUtc.ToString("O")))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task<Guid> SeedWorkstreamAsync(IProgrammeRepository repository, Guid tenant)
    {
        var programme = await repository.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(), Name = "Batch programme", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        var project = await repository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey, Name = "Batch project", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        var workstream = await repository.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Name = "Batch workstream", CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        return workstream.WorkstreamKey;
    }

    private static async Task<Guid> SeedWorkItemAsync(IProgrammeRepository repository, Guid tenant)
    {
        var item = await repository.UpsertWorkItemAsync(new WorkItem
        {
            WorkItemKey = Guid.NewGuid(), WorkstreamKey = await SeedWorkstreamAsync(repository, tenant), Title = "Batch item",
            Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = Now, UpdatedAtUtc = Now
        }, tenant);
        return item.WorkItemKey;
    }
}
