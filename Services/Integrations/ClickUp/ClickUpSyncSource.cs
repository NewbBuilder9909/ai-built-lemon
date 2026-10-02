using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// Presents the ClickUp integration to everything above the boundary. A thin
/// adapter on purpose: all the orchestration (lease, run state, idempotent
/// upserts, audit, retention) stays in <see cref="ClickUpSyncService"/>, and
/// this only translates its vendor-shaped result into a
/// <see cref="SyncOutcome"/>. Nothing outside this folder references
/// IClickUpSyncService any more.
/// </summary>
public sealed class ClickUpSyncSource(IClickUpSyncService syncService) : ISyncSource
{
    public string Name => ClickUpSyncService.SourceName;

    public string DisplayName => "ClickUp";

    public string FeatureKey => ProductFeature.ClickUpSync;

    /// <summary>
    /// ClickUp supplies tasks (Work) and time entries (Time). It carries no
    /// leave/absence data and no commercial data, so a capacity figure built
    /// from ClickUp alone is knowingly missing both.
    /// </summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Work | SourceCapabilities.Time;

    public async Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var result = await syncService.RunAsync(tenantId, triggeredByMemberId, cancellationToken);
        return new SyncOutcome(result.Describe(), result.UnresolvedPeople);
    }
}
