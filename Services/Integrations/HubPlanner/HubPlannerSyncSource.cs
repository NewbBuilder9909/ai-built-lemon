using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// Presents the Hub Planner integration to everything above the boundary —
/// the same thin-adapter shape as <see cref="ClickUp.ClickUpSyncSource"/>;
/// see that class for why the orchestration stays in the sync service.
/// </summary>
public sealed class HubPlannerSyncSource(IHubPlannerSyncService syncService) : ISyncSource
{
    public string Name => HubPlannerSyncService.SourceName;

    public string DisplayName => "Hub Planner";

    public string FeatureKey => ProductFeature.HubPlannerSync;

    /// <summary>
    /// Hub Planner supplies people and their scheduled bookings (Resource).
    /// Deliberately not Work: a booking is intent to spend time, never
    /// evidence that work was delivered — see Models/Programme/PlannedAllocation.
    /// Its bookings are also not Time: planned hours are not recorded actuals.
    /// </summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Resource;

    public async Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var result = await syncService.RunAsync(tenantId, triggeredByMemberId, cancellationToken);
        return new SyncOutcome(result.Describe(), result.UnresolvedResources);
    }
}
