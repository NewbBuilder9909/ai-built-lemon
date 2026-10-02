using Microsoft.Extensions.DependencyInjection;
using NPoco;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Phase 2 replaced "read the tenant's whole time history, filter in memory"
/// with bounded SQL reads (period seek, summed actuals, coverage facts) and
/// replaced per-person loops with one batched read. Each interface method has
/// an in-memory default that the unit-test fakes use, so the unit suite
/// proves the reports against the default semantics. These tests prove the
/// real SQL overrides answer exactly the same questions, including the edge
/// rows: an entry dated only by its UTC start (just before midnight), an
/// undated entry, and another tenant's rows sharing a person's key.
/// Every test uses fresh tenant ids so its sums are exact.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BoundedReadIntegrationTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string EvidenceNotProduced = "the SQL bounded reads were not compared with their in-memory definitions.";

    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    private T Resolve<T>() where T : notnull => factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Period_coverage_and_summed_reads_match_their_in_memory_definitions()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IProgrammeRepository>();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var workItem = await SeedWorkItemAsync(repository, tenant);

        var inByWorkDate = await AddEntryAsync(repository, tenant, workItem, alice, 2m, workDate: new DateOnly(2026, 3, 10));
        var inByStartOnly = await AddEntryAsync(repository, tenant, workItem, bob, 3m, startedAtUtc: new DateTime(2026, 3, 31, 23, 30, 0, DateTimeKind.Utc));
        await AddEntryAsync(repository, tenant, null, alice, 1.5m, workDate: new DateOnly(2026, 4, 1));
        await AddEntryAsync(repository, tenant, workItem, alice, 4m);
        await AddEntryAsync(repository, tenant, workItem, alice, 0.5m, workDate: new DateOnly(2026, 2, 28), source: "Tempo");
        await AddEntryAsync(repository, otherTenant, null, alice, 10m, source: "ClickUp");
        await AddEntryAsync(repository, otherTenant, null, alice, 7m, workDate: new DateOnly(2026, 3, 15), source: "ClickUp");

        var all = await repository.GetTimeEntriesAsync(tenant);

        // Period: the SQL seek returns what ReportDate filtering returns,
        // including the entry dated only by its start instant.
        var period = await repository.GetTimeEntriesAsync(tenant, PeriodStart, PeriodEnd);
        var expectedPeriod = all.Where(e => e.ReportDate is { } d && d >= PeriodStart && d <= PeriodEnd).Select(e => e.TimeEntryKey).Order();
        Assert.Equal(expectedPeriod, period.Select(e => e.TimeEntryKey).Order());
        Assert.Equal(new[] { inByWorkDate, inByStartOnly }.Order(), period.Select(e => e.TimeEntryKey).Order());

        // Coverage: tenant-wide footnote facts, never another tenant's.
        Assert.Equal(new TimeEntryCoverage(4m, HasTempoEntries: true), await repository.GetTimeEntryCoverageAsync(tenant));
        Assert.Equal(new TimeEntryCoverage(10m, HasTempoEntries: false), await repository.GetTimeEntryCoverageAsync(otherTenant));

        // Presence: the SQL EXISTS pair agrees with its definition over the
        // list reads, for work items and time, and never sees another tenant.
        var seededSource = (await repository.GetWorkItemByKeyAsync(workItem, tenant))!.ExternalSource;
        var probes = new List<(Guid Tenant, string Source)> { (tenant, "Tempo"), (tenant, "ClickUp"), (otherTenant, "ClickUp"), (otherTenant, "Tempo") };
        if (seededSource is not null) probes.AddRange([(tenant, seededSource), (otherTenant, seededSource)]);
        foreach (var (probeTenant, source) in probes)
        {
            var expected = new SourceDataPresence(
                (await repository.GetWorkItemsAsync(probeTenant)).Any(i => i.ExternalSource == source),
                (await repository.GetTimeEntriesAsync(probeTenant)).Any(e => e.ExternalSource == source));
            Assert.Equal(expected, await repository.GetSourcePresenceAsync(probeTenant, source));
        }
        Assert.Equal(new SourceDataPresence(false, true), await repository.GetSourcePresenceAsync(tenant, "Tempo"));
        Assert.False((await repository.GetSourcePresenceAsync(otherTenant, "Tempo")).Any);

        // Source read: exactly one source's rows, never another source's or tenant's.
        Assert.Equal(all.Where(e => e.ExternalSource == "Tempo").Select(e => e.TimeEntryKey).Order(),
            (await repository.GetTimeEntriesBySourceAsync(tenant, "Tempo")).Select(e => e.TimeEntryKey).Order());
        Assert.Empty(await repository.GetTimeEntriesBySourceAsync(otherTenant, "Tempo"));

        // Summed actuals: all history for effort variance, one person for My Work.
        Assert.Equal(new Dictionary<Guid, decimal> { [workItem] = 9.5m }, await repository.GetLoggedHoursByWorkItemAsync(tenant));
        Assert.Equal(new Dictionary<Guid, decimal> { [workItem] = 6.5m }, await repository.GetLoggedHoursByWorkItemAsync(tenant, alice));
        Assert.Equal(new Dictionary<Guid, decimal> { [workItem] = 3m }, await repository.GetLoggedHoursByWorkItemAsync(tenant, bob));
        Assert.Empty(await repository.GetLoggedHoursByWorkItemAsync(otherTenant));

        // Point read: one work item's entries, and none through another tenant.
        Assert.Equal(4, (await repository.GetTimeEntriesForWorkItemAsync(workItem, tenant)).Count);
        Assert.Empty(await repository.GetTimeEntriesForWorkItemAsync(workItem, otherTenant));
        Assert.Equal(workItem, (await repository.GetWorkItemByKeyAsync(workItem, tenant))?.WorkItemKey);
        Assert.Null(await repository.GetWorkItemByKeyAsync(workItem, otherTenant));
    }

    [Fact]
    public async Task Weekly_project_counts_match_the_in_memory_series_Delivery_Load_used_to_build()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IProgrammeRepository>();
        var tenant = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var projectOneItem = await SeedWorkItemAsync(repository, tenant);
        var projectTwoItem = await SeedWorkItemAsync(repository, tenant);

        // Week of Mon 9 March: Alice on two projects (three entries).
        await AddEntryAsync(repository, tenant, projectOneItem, alice, 1m, workDate: new DateOnly(2026, 3, 9));
        await AddEntryAsync(repository, tenant, projectOneItem, alice, 1m, workDate: new DateOnly(2026, 3, 10));
        await AddEntryAsync(repository, tenant, projectTwoItem, alice, 1m, workDate: new DateOnly(2026, 3, 15));
        // Week of Mon 30 March: Bob by UTC start only; Alice logged but unlinked (zero contexts).
        await AddEntryAsync(repository, tenant, projectOneItem, bob, 1m, startedAtUtc: new DateTime(2026, 3, 31, 23, 30, 0, DateTimeKind.Utc));
        await AddEntryAsync(repository, tenant, null, alice, 1m, workDate: new DateOnly(2026, 4, 1), source: "Tempo");
        // Excluded: undated, and dated but with no person.
        await AddEntryAsync(repository, tenant, projectOneItem, alice, 1m);
        await AddEntryAsync(repository, tenant, projectOneItem, null, 1m, workDate: new DateOnly(2026, 3, 9), source: "NoPerson");

        var expected = new[]
        {
            new WeeklyProjectCount(alice, new DateOnly(2026, 3, 9), 2),
            new WeeklyProjectCount(alice, new DateOnly(2026, 3, 30), 0),
            new WeeklyProjectCount(bob, new DateOnly(2026, 3, 30), 1),
        };
        var actual = await repository.GetWeeklyProjectCountsAsync(tenant);
        Assert.Equal(
            expected.OrderBy(w => w.StaffKey).ThenBy(w => w.WeekStart),
            actual.OrderBy(w => w.StaffKey).ThenBy(w => w.WeekStart));

        Assert.Equal(["IntegrationTest", "Tempo"], (await repository.GetDatedTimeSourcesAsync(tenant)).Order());
    }

    [Fact]
    public async Task A_cancelled_request_stops_the_read_at_the_database()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var tenant = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Resolve<IProgrammeRepository>().GetTimeEntriesAsync(tenant, PeriodStart, PeriodEnd, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Resolve<IReportingQueryService>().BuildHubAsync(PeriodStart, PeriodEnd, null, tenant, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task Batched_person_reads_match_the_single_person_reads_and_map_every_key()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var rates = Resolve<IStaffRateRepository>();
        var hours = Resolve<IWorkHoursHistoryRepository>();
        var availability = Resolve<IAvailabilityRepository>();
        var withHistory = Guid.NewGuid();
        var withNothing = Guid.NewGuid();
        var admin = Guid.NewGuid();

        await rates.SetCurrentRateAsync(withHistory, 50m, "GBP", admin);
        await rates.SetCurrentRateAsync(withHistory, 55m, "GBP", admin);
        await hours.SetCurrentHoursAsync(withHistory, 37.5m, admin);
        await hours.SetCurrentHoursAsync(withHistory, 22.5m, admin);
        foreach (var date in new[] { new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3), new DateOnly(2026, 4, 1) })
        {
            await availability.CreateAsync(new Availability
            {
                StaffKey = withHistory, Date = date, StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue,
                Status = AvailabilityStatus.Holiday, Source = AvailabilitySource.LeavePolicy
            });
        }

        var keys = new[] { withHistory, withNothing, withHistory };

        var rateBatch = await rates.GetHistoryAsync(keys);
        Assert.Equal(2, rateBatch.Count);
        Assert.Equal((await rates.GetHistoryAsync(withHistory)).Select(r => r.CostPerHour), rateBatch[withHistory].Select(r => r.CostPerHour));
        Assert.Equal([55m, 50m], rateBatch[withHistory].Select(r => r.CostPerHour));
        Assert.Empty(rateBatch[withNothing]);

        var hoursBatch = await hours.GetHistoryAsync(keys);
        Assert.Equal((await hours.GetHistoryAsync(withHistory)).Select(h => h.HoursPerWeek), hoursBatch[withHistory].Select(h => h.HoursPerWeek));
        Assert.Empty(hoursBatch[withNothing]);

        var leaveBatch = await availability.GetForStaffAsync(keys, PeriodStart, PeriodEnd);
        var single = await availability.GetForStaffAsync(withHistory, PeriodStart, PeriodEnd);
        Assert.Equal(2, single.Count);
        Assert.Equal(single.Select(a => a.Date), leaveBatch[withHistory].Select(a => a.Date));
        Assert.Empty(leaveBatch[withNothing]);
    }

    [Fact]
    public async Task Rates_sharing_a_timestamp_come_back_in_one_order_from_both_reads()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        // effectiveFromUtc is `datetime` (about 3 ms), so two rate changes in
        // quick succession tie. This used to come back in either order, and
        // made the batched-read test above fail intermittently.
        var person = Guid.NewGuid();
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>().CreateScope())
        {
            foreach (var cost in new[] { 40m, 41m, 42m })
            {
                await scope.Database.ExecuteAsync(
                    "INSERT INTO StaffOps_StaffRate (staffKey, costPerHour, rateCurrency, effectiveFromUtc, changedByStaffKey, changedAtUtc) VALUES (@0, @1, 'GBP', @2, @0, @2)",
                    person, cost, at);
            }

            scope.Complete();
        }

        var rates = Resolve<IStaffRateRepository>();
        var single = await rates.GetHistoryAsync(person);
        var batched = (await rates.GetHistoryAsync([person]))[person];

        Assert.Equal([42m, 41m, 40m], single.Select(r => r.CostPerHour));
        Assert.Equal(single.Select(r => r.CostPerHour), batched.Select(r => r.CostPerHour));
    }

    [Fact]
    public async Task Search_matches_its_in_memory_definition_treats_wildcards_as_text_and_stays_in_the_tenant()
    {
        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail(EvidenceNotProduced, output)) return;

        var repository = Resolve<IProgrammeRepository>();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await SeedWorkItemAsync(repository, tenant, "Release 50% signed off", "REL-1");
        await SeedWorkItemAsync(repository, tenant, "Release_notes draft", "REL-2");
        await SeedWorkItemAsync(repository, tenant, "HL7 [feed] mapping", "INT-7");
        await SeedWorkItemAsync(repository, tenant, "Unrelated", "ZZ-9");
        await SeedWorkItemAsync(repository, otherTenant, "Release 50% signed off", "REL-1");

        var all = await repository.GetWorkItemsAsync(tenant);
        foreach (var term in new[] { "release", "50%", "_notes", "[feed]", "rel-", "zz-9", "nothing like this" })
        {
            var expected = all
                .Where(i => i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (i.ExternalId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(i => i.WorkItemKey).Order();
            var actual = await repository.SearchWorkItemsAsync(tenant, term, 100);

            Assert.Equal(expected, actual.Select(i => i.WorkItemKey).Order());
        }

        Assert.Single(await repository.SearchWorkItemsAsync(tenant, "50%", 100));
        Assert.Single(await repository.SearchWorkItemsAsync(tenant, "release", 1));
    }

    private static async Task<Guid> SeedWorkItemAsync(IProgrammeRepository repository, Guid tenant, string title = "Bounded read item", string? externalId = null)
    {
        var now = DateTime.UtcNow;
        var programme = await repository.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(), Name = "Bounded read programme", CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var project = await repository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey, Name = "Bounded read project", CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var workstream = await repository.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Name = "Bounded read workstream", CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        var item = await repository.UpsertWorkItemAsync(new WorkItem
        {
            WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstream.WorkstreamKey, Title = title,
            ExternalSource = externalId is null ? null : "IntegrationTest", ExternalId = externalId,
            Stage = WorkItemLifecycleStage.InProgress, CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        return item.WorkItemKey;
    }

    private static async Task<Guid> AddEntryAsync(
        IProgrammeRepository repository, Guid tenant, Guid? workItem, Guid? staffKey, decimal hours,
        DateOnly? workDate = null, DateTime? startedAtUtc = null, string source = "IntegrationTest")
    {
        var now = DateTime.UtcNow;
        var entry = await repository.UpsertTimeEntryAsync(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(), WorkItemKey = workItem, StaffKey = staffKey, DurationHours = hours,
            WorkDate = workDate, StartedAtUtc = startedAtUtc, ExternalSource = source,
            ExternalId = Guid.NewGuid().ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenant);
        return entry.TimeEntryKey;
    }
}
