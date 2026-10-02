using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Integration;

/// <summary>
/// Phase 0 of docs/architecture-review-2026-09-24.md: measure the
/// scalability findings before changing anything, so Phase 2 has a number to
/// beat rather than a claim.
///
/// Seeds two tenants with *identical current-month activity* but different
/// history depth: one with <see cref="HistoryMonthsSmall"/> months of time
/// entries, one with five times that. Then times the Gold entry points each
/// page calls. Finding A1 predicts the second tenant is slower, although
/// every page shows the same period. A page whose cost is bounded by what it
/// shows would time the same for both. That ratio is the baseline's headline.
///
/// Opt-in (PP_RUN_SCALE_BASELINE=1): it inserts several hundred thousand rows
/// and is a measurement, not a regression gate, so it does not belong in
/// every CI run. It asserts only that the seed landed. Timings are written
/// to the test output and to artifacts/scale-baseline/*.json. All seeded rows
/// are deleted afterwards, success or failure.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ScaleBaselineTests(ProgrammePulseWebApplicationFactory factory, ITestOutputHelper output)
{
    private const string OptInVariable = "PP_RUN_SCALE_BASELINE";

    private const int Staff = 60;
    private const int Programmes = 10;
    private const int ProjectsPerProgramme = 4;
    private const int WorkstreamsPerProject = 5;
    private const int WorkItemsPerWorkstream = 100;
    private const int TimeEntriesPerMonth = 10_000;
    private const int HistoryMonthsSmall = 6;
    private const int HistoryMonthsLarge = HistoryMonthsSmall * 5;
    private const int Repetitions = 3;

    [Fact]
    public async Task Gold_query_cost_against_history_depth()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            output.WriteLine($"Scale baseline not run: set {OptInVariable}=1 to measure (it seeds ~400k rows).");
            return;
        }

        if (!ProgrammePulseWebApplicationFactory.HasDatabaseOrFail("no scale baseline was measured.", output))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var small = Guid.NewGuid();
        var large = Guid.NewGuid();

        try
        {
            var seedTimer = Stopwatch.StartNew();
            await SeedAsync(small, tenantIndex: 1, HistoryMonthsSmall, today);
            await SeedAsync(large, tenantIndex: 2, HistoryMonthsLarge, today);
            output.WriteLine($"Seeded both tenants in {seedTimer.Elapsed.TotalSeconds:F1}s.");

            var results = new List<object>();
            foreach (var (label, tenantId, months) in new[] { ("1x history", small, HistoryMonthsSmall), ("5x history", large, HistoryMonthsLarge) })
            {
                var counts = await CountAsync(tenantId);
                Assert.Equal(TimeEntriesPerMonth * months, counts["TimeEntry"]);

                var timings = new Dictionary<string, object>
                {
                    ["ReportingHub (current month)"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IReportingQueryService>().BuildHubAsync(monthStart, today, null, tenantId)),
                    ["CostSummary (current month)"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IReportingQueryService>().BuildCostSummaryAsync(monthStart, today, tenantId)),
                    ["ProgrammeOverview"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IProgrammeOverviewQueryService>().BuildOverviewAsync(tenantId)),
                    ["DeliveryLoad"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IDeliveryLoadQueryService>().BuildAsync(tenantId, includePeople: true)),

                    // Breakdown: the individual reads the pages above are built
                    // from, so a slow page can be attributed rather than guessed.
                    ["read: WorkItems"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IProgrammeRepository>().GetWorkItemsAsync(tenantId)),
                    ["read: TimeEntries (all)"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IProgrammeRepository>().GetTimeEntriesAsync(tenantId)),
                    ["read: TimeEntries (period)"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IProgrammeRepository>().GetTimeEntriesAsync(tenantId, monthStart, today)),
                    ["read: Allocations"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IProgrammeRepository>().GetAllocationsAsync(tenantId)),
                    ["read: Staff roster"] = await MeasureAsync(sp =>
                        sp.GetRequiredService<IStaffRepository>().GetByTenantAsync(tenantId)),
                };

                results.Add(new { label, historyMonths = months, rows = counts, timings });
                output.WriteLine($"--- {label}: {string.Join(", ", counts.Select(c => $"{c.Key}={c.Value:N0}"))}");
                foreach (var (name, timing) in timings)
                {
                    output.WriteLine($"    {name,-30} {timing}");
                }
            }

            results.Add(new { label = "sync writes (1,000 tasks x 2 assignees, 1,000 time entries)", timings = await MeasureSyncWritesAsync(small) });

            await WriteEvidenceAsync(results);
        }
        finally
        {
            await CleanUpAsync(small, large);
        }
    }

    /// <summary>
    /// Finding A3: the same writes through the single-row calls sync used to
    /// make per task (upsert, then replace allocations) and through the
    /// batched calls it makes now, into fresh workstreams of the seeded tenant.
    /// Each path is timed on a first write (inserts) and a re-sync (updates).
    /// </summary>
    private async Task<Dictionary<string, object>> MeasureSyncWritesAsync(Guid tenantId)
    {
        const int Tasks = 1000;
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var now = DateTime.UtcNow;
        var project = (await repository.GetProjectsAsync(tenantId))[0];
        var people = (await factory.Services.CreateScope().ServiceProvider.GetRequiredService<IStaffRepository>().GetByTenantAsync(tenantId))
            .Select(s => s.StaffKey).ToArray();

        async Task<Guid> NewWorkstreamAsync(string name) => (await repository.UpsertWorkstreamAsync(new ProgrammePulse.Models.Programme.Workstream
        {
            WorkstreamKey = Guid.NewGuid(), ProjectKey = project.ProjectKey, Name = name, CreatedAtUtc = now, UpdatedAtUtc = now
        }, tenantId)).WorkstreamKey;

        ProgrammePulse.Models.Programme.WorkItem Item(string prefix, int i, Guid workstream) => new()
        {
            WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstream, Title = $"{prefix} task {i}",
            Stage = ProgrammePulse.Models.Programme.WorkItemLifecycleStage.InProgress, EstimatedHours = 4m,
            ExternalSource = "ScaleBaseline", ExternalId = $"{prefix}-{i}", CreatedAtUtc = now, UpdatedAtUtc = now
        };

        Guid[] Assignees(int i) => [people[i % people.Length], people[(i + 1) % people.Length]];

        ProgrammePulse.Models.Programme.TimeEntry Entry(string prefix, int i, Guid workItem) => new()
        {
            TimeEntryKey = Guid.NewGuid(), WorkItemKey = workItem, StaffKey = people[i % people.Length], DurationHours = 1.5m,
            WorkDate = DateOnly.FromDateTime(now), ExternalSource = "ScaleBaseline", ExternalId = $"{prefix}-te-{i}",
            CreatedAtUtc = now, UpdatedAtUtc = now
        };

        var timings = new Dictionary<string, object>();
        var singleWorkstream = await NewWorkstreamAsync("Single-row writes");
        var batchWorkstream = await NewWorkstreamAsync("Batched writes");
        var anyItem = (await repository.GetWorkItemsAsync(tenantId))[0].WorkItemKey;

        foreach (var pass in new[] { "insert", "re-sync" })
        {
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < Tasks; i++)
            {
                var saved = await repository.UpsertWorkItemAsync(Item("single", i, singleWorkstream), tenantId);
                await repository.UpsertWorkItemAllocationsAsync(saved.WorkItemKey, Assignees(i), now, tenantId);
            }

            timings[$"tasks single-row ({pass})"] = $"{timer.Elapsed.TotalMilliseconds:F0} ms";

            timer.Restart();
            foreach (var chunk in Enumerable.Range(0, Tasks).Chunk(200))
            {
                await repository.UpsertWorkItemsAsync(chunk.Select(i => new WorkItemUpsert(Item("batch", i, batchWorkstream), Assignees(i))).ToList(), now, tenantId);
            }

            timings[$"tasks batched ({pass})"] = $"{timer.Elapsed.TotalMilliseconds:F0} ms";

            timer.Restart();
            for (var i = 0; i < Tasks; i++)
            {
                await repository.UpsertTimeEntryAsync(Entry("single", i, anyItem), tenantId);
            }

            timings[$"time entries single-row ({pass})"] = $"{timer.Elapsed.TotalMilliseconds:F0} ms";

            timer.Restart();
            foreach (var chunk in Enumerable.Range(0, Tasks).Chunk(200))
            {
                await repository.UpsertTimeEntriesAsync(chunk.Select(i => Entry("batch", i, anyItem)).ToList(), tenantId);
            }

            timings[$"time entries batched ({pass})"] = $"{timer.Elapsed.TotalMilliseconds:F0} ms";
        }

        output.WriteLine("--- sync writes (1,000 tasks x 2 assignees, 1,000 time entries):");
        foreach (var (name, timing) in timings)
        {
            output.WriteLine($"    {name,-36} {timing}");
        }

        return timings;
    }

    private async Task<Timing> MeasureAsync(Func<IServiceProvider, Task> query)
    {
        var elapsed = new List<double>();
        long allocated = 0;
        for (var i = 0; i < Repetitions; i++)
        {
            using var scope = factory.Services.CreateScope();
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            await query(scope.ServiceProvider);
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        }

        elapsed.Sort();
        return new Timing(Math.Round(elapsed[elapsed.Count / 2], 1), Math.Round(elapsed[0], 1), Math.Round(elapsed[^1], 1), allocated / (1024 * 1024));
    }

    private sealed record Timing(double MedianMs, double MinMs, double MaxMs, long AllocatedMb)
    {
        public override string ToString() => $"median {MedianMs,8:F1} ms (min {MinMs:F1}, max {MaxMs:F1}), ~{AllocatedMb} MB allocated";
    }

    private async Task SeedAsync(Guid tenantId, int tenantIndex, int historyMonths, DateOnly today)
    {
        // Values are inlined rather than parameterised: all are generated
        // here (Guids, integers, a date), and NPoco would otherwise treat the
        // T-SQL variables as its own named parameters.
        var t = $"'{tenantId}'";
        var now = $"'{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}'";
        var projects = Programmes * ProjectsPerProgramme;
        var workstreams = projects * WorkstreamsPerProject;
        var workItems = workstreams * WorkItemsPerWorkstream;
        var entries = TimeEntriesPerMonth * historyMonths;
        var days = historyMonths * 30;
        var todayLiteral = $"'{today:yyyy-MM-dd}'";
        const string Numbers = "SELECT TOP ({0}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c";
        string N(int count) => string.Format(CultureInfo.InvariantCulture, Numbers, count);

        string[] statements =
        [
            $"""
            INSERT INTO StaffOps_Staff (staffKey, memberId, fullName, email, team, isActive, defaultWorkHoursPerWeek, tenantId, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), -({tenantIndex} * 1000000 + n.i + 1), CONCAT('Scale Person ', n.i), CONCAT('scale-{tenantIndex}-', n.i, '-{tenantId:N}', CHAR(64), 'scale.test'),
                   CONCAT('Team ', n.i % 6), 1, 37.5, {t}, {now}, {now}
            FROM ({N(Staff)}) n
            """,
            $"""
            INSERT INTO ProgrammeOps_Programme (programmeKey, tenantId, name, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), {t}, CONCAT('Programme ', n.i), {now}, {now} FROM ({N(Programmes)}) n
            """,
            $"""
            INSERT INTO ProgrammeOps_Project (projectKey, tenantId, programmeKey, name, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), {t}, p.programmeKey, CONCAT('Project ', n.i), {now}, {now}
            FROM ({N(projects)}) n
            JOIN (SELECT programmeKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM ProgrammeOps_Programme WHERE tenantId = {t}) p ON p.r = n.i % {Programmes}
            """,
            $"""
            INSERT INTO ProgrammeOps_Workstream (workstreamKey, tenantId, projectKey, name, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), {t}, p.projectKey, CONCAT('Workstream ', n.i), {now}, {now}
            FROM ({N(workstreams)}) n
            JOIN (SELECT projectKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM ProgrammeOps_Project WHERE tenantId = {t}) p ON p.r = n.i % {projects}
            """,
            $"""
            INSERT INTO ProgrammeOps_WorkItem (workItemKey, tenantId, workstreamKey, title, stage, isMilestone, assignedStaffKey, dueDateUtc, estimatedHours, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), {t}, w.workstreamKey, CONCAT('Work item ', n.i),
                   CASE n.i % 7 WHEN 0 THEN 'Backlog' WHEN 1 THEN 'Ready' WHEN 2 THEN 'InProgress' WHEN 3 THEN 'Blocked' WHEN 4 THEN 'InReview' ELSE 'Done' END,
                   0, s.staffKey, DATEADD(day, n.i % 90, {todayLiteral}), 8, {now}, {now}
            FROM ({N(workItems)}) n
            JOIN (SELECT workstreamKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM ProgrammeOps_Workstream WHERE tenantId = {t}) w ON w.r = n.i % {workstreams}
            JOIN (SELECT staffKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM StaffOps_Staff WHERE tenantId = {t}) s ON s.r = n.i % {Staff}
            """,
            // Spread evenly over the history window, so the last 30 days hold
            // the same TimeEntriesPerMonth in both tenants.
            $"""
            INSERT INTO ProgrammeOps_TimeEntry (timeEntryKey, tenantId, workItemKey, staffKey, durationHours, workDate, startedAtUtc, isBillable, billabilityKnown, createdAtUtc, updatedAtUtc)
            SELECT NEWID(), {t}, wi.workItemKey, s.staffKey, 1.5,
                   DATEADD(day, -(n.i % {days}), {todayLiteral}), DATEADD(day, -(n.i % {days}), {todayLiteral}), 1, 1, {now}, {now}
            FROM ({N(entries)}) n
            JOIN (SELECT workItemKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM ProgrammeOps_WorkItem WHERE tenantId = {t}) wi ON wi.r = n.i % {workItems}
            JOIN (SELECT staffKey, ROW_NUMBER() OVER (ORDER BY id) - 1 AS r FROM StaffOps_Staff WHERE tenantId = {t}) s ON s.r = n.i % {Staff}
            """,
        ];

        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope();
        scope.Database.CommandTimeout = 600;
        foreach (var statement in statements)
        {
            await scope.Database.ExecuteAsync(statement);
        }

        scope.Complete();
    }

    private static readonly (string Label, string Table)[] SeededTables =
    [
        ("TimeEntry", "ProgrammeOps_TimeEntry"),
        ("WorkItem", "ProgrammeOps_WorkItem"),
        ("Workstream", "ProgrammeOps_Workstream"),
        ("Project", "ProgrammeOps_Project"),
        ("Programme", "ProgrammeOps_Programme"),
        ("Staff", "StaffOps_Staff"),
    ];

    private async Task<Dictionary<string, int>> CountAsync(Guid tenantId)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        var counts = new Dictionary<string, int>();
        foreach (var (label, table) in SeededTables)
        {
            counts[label] = await scope.Database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE tenantId = '{tenantId}'");
        }

        return counts;
    }

    private async Task CleanUpAsync(params Guid[] tenants)
    {
        using var scope = factory.Services.GetRequiredService<IScopeProvider>().CreateScope();
        scope.Database.CommandTimeout = 600;
        foreach (var tenantId in tenants)
        {
            foreach (var table in new[] { "ProgrammeOps_Alert", "ProgrammeOps_WorkItemAllocation" }.Concat(SeededTables.Select(s => s.Table)))
            {
                await scope.Database.ExecuteAsync($"DELETE FROM {table} WHERE tenantId = '{tenantId}'");
            }
        }

        scope.Complete();
    }

    private async Task WriteEvidenceAsync(IEnumerable<object> results)
    {
        var directory = Path.Combine(Architecture.SourceTree.Root, "artifacts", "scale-baseline");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"scale-baseline-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            measuredAtUtc = DateTime.UtcNow,
            machine = Environment.MachineName,
            shape = new { Staff, Programmes, ProjectsPerProgramme, WorkstreamsPerProject, WorkItemsPerWorkstream, TimeEntriesPerMonth, Repetitions },
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"Evidence: {path}");
    }
}
