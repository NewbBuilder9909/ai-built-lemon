using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Tests.ProgrammeOps;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Integrations.ClickUp;
using ProgrammePulse.Services.Integrations.HubPlanner;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// SourceIndependenceTests proves *statically* that Silver/Gold can't reach a
/// vendor namespace. This proves the same boundary *behaviourally*: build a
/// real ISyncSourceRegistry from the two real adapters (ClickUpSyncSource,
/// HubPlannerSyncSource — each wrapping a trivial fake sync service, so no
/// HTTP or database is involved) plus a third, hypothetical source
/// (FakeSyncSource, standing in for e.g. Jira — deliberately not a new real
/// connector, per this phase's exit gate), then drives that registry through
/// the real SyncStatusQueryService exactly as StaffProgrammeOverviewController
/// does. All three sources must appear with zero source-specific code in
/// either the registry construction or the status query — the third source
/// is indistinguishable from the two real ones to everything above the
/// boundary.
/// </summary>
public sealed class SourceIndependenceDemonstrationTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeClickUpSyncService : IClickUpSyncService
    {
        public Task<ClickUpSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClickUpSyncResult(1, 1, 1, 1, 1));
    }

    private sealed class FakeHubPlannerSyncService : IHubPlannerSyncService
    {
        public Task<HubPlannerSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HubPlannerSyncResult(1, 1, 0, 0));
    }

    /// <summary>
    /// The real ClickUp/Hub Planner adapters, a hypothetical third source,
    /// and nothing else — this is deliberately built the same way
    /// ProgrammeOperationsComposer would (one AddScoped<ISyncSource, ...>
    /// per source), just without a DI container.
    /// </summary>
    private static SyncSourceRegistry BuildRegistryWithThreeSources() => new(
    [
        new ClickUpSyncSource(new FakeClickUpSyncService()),
        new HubPlannerSyncSource(new FakeHubPlannerSyncService()),
        new FakeSyncSource("Jira", "Jira", "jira-sync")
    ]);

    [Fact]
    public void The_registry_lists_the_two_real_sources_and_the_hypothetical_third_one_identically()
    {
        var registry = BuildRegistryWithThreeSources();

        Assert.Equal(["ClickUp", "Hub Planner", "Jira"], registry.All.Select(s => s.DisplayName).ToArray());
    }

    /// <summary>
    /// The exact call StaffProgrammeOverviewController.Index makes to build
    /// the "Data sources" panel — proving the third source's row is produced
    /// by the same source-agnostic code path as the two real ones, not a
    /// special case.
    /// </summary>
    [Fact]
    public async Task The_publication_status_service_reports_all_three_sources_with_no_source_specific_code()
    {
        var statusService = new SyncStatusQueryService(new FakeSyncRunRepository(), BuildRegistryWithThreeSources(), new FixedTimeProvider(Now));

        var states = await statusService.GetPublicationStatesAsync(TestTenantId);

        Assert.Equal(["ClickUp", "Hub Planner", "Jira"], states.Select(s => s.DisplayName).ToArray());
        Assert.All(states, s => Assert.Equal("Never published", s.StatusLabel));
    }

    /// <summary>
    /// The exact call StaffProgrammeOverviewController.Sync makes: resolve by
    /// route segment, run it, translate to a source-neutral SyncOutcome. Runs
    /// all three — including the two real adapters — through the identical
    /// call shape a controller uses, proving ISyncSource.RunAsync's contract
    /// holds for a source the caller has never heard of.
    /// </summary>
    [Fact]
    public async Task Every_source_including_the_hypothetical_third_one_runs_through_the_same_polymorphic_call()
    {
        var registry = BuildRegistryWithThreeSources();

        foreach (var name in new[] { "ClickUp", "HubPlanner", "Jira" })
        {
            var source = registry.Find(name);
            Assert.NotNull(source);

            var outcome = await source!.RunAsync(TestTenantId, triggeredByMemberId: null);

            Assert.False(string.IsNullOrWhiteSpace(outcome.Summary));
        }
    }
}
