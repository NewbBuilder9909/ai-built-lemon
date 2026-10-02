using Microsoft.Extensions.Logging;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>What a person sees after asking for a programme sync.</summary>
public sealed record ProgrammeSyncResult(bool Succeeded, string Message);

/// <summary>
/// Running a programme source sync on request, and what happens around it:
/// which sources the tenant's plan entitles, the message the person sees,
/// and alert detection once new data has landed. Moved out of
/// StaffProgrammeOverviewController. The controller keeps only the HTTP
/// decisions (404 for an unknown source before the tenant is resolved, 403
/// for an unresolved tenant or a source outside the plan), because their
/// order is part of what the endpoint promises.
/// </summary>
public interface IProgrammeSyncService
{
    ISyncSource? Find(string source);

    Task<bool> IsEntitledAsync(ISyncSource source);

    /// <summary>The sync buttons the current tenant's plan entitles them to.</summary>
    Task<IReadOnlyList<SyncSourceOptionViewModel>> GetEntitledSourcesAsync();

    Task<int> CountUnresolvedIdentitiesAsync(Guid tenantId);

    Task<ProgrammeSyncResult> RunAsync(ISyncSource source, Guid tenantId, int? triggeredByMemberId);
}

public sealed class ProgrammeSyncService(
    ISyncSourceRegistry sourceRegistry,
    IFeatureGate featureGate,
    IIdentityResolutionRepository identityResolutionRepository,
    IAlertDetectionService alertDetectionService,
    TimeProvider timeProvider,
    ILogger<ProgrammeSyncService> logger) : IProgrammeSyncService
{
    public ISyncSource? Find(string source) => sourceRegistry.Find(source);

    public Task<bool> IsEntitledAsync(ISyncSource source) => featureGate.IsEnabledAsync(source.FeatureKey);

    public async Task<IReadOnlyList<SyncSourceOptionViewModel>> GetEntitledSourcesAsync()
    {
        // A source the plan excludes is omitted rather than rendered and then
        // refused. The action re-checks regardless, so this is presentation,
        // not the gate.
        var options = new List<SyncSourceOptionViewModel>();
        foreach (var source in sourceRegistry.All)
        {
            if (await featureGate.IsEnabledAsync(source.FeatureKey))
            {
                options.Add(new SyncSourceOptionViewModel(source.Name, source.DisplayName));
            }
        }

        return options;
    }

    public async Task<int> CountUnresolvedIdentitiesAsync(Guid tenantId) =>
        await identityResolutionRepository.CountUnresolvedAsync(tenantId);

    public async Task<ProgrammeSyncResult> RunAsync(ISyncSource source, Guid tenantId, int? triggeredByMemberId)
    {
        SyncOutcome outcome;
        try
        {
            outcome = await source.RunAsync(tenantId, triggeredByMemberId);
        }
        catch (InvalidOperationException ex)
        {
            return new ProgrammeSyncResult(false, ex.Message);
        }

        await DetectAlertsAsync(source, tenantId);

        var hint = outcome.UnresolvedPeople > 0
            ? " Review the identity queue to match them to staff (ambiguous ones need an explicit link)."
            : string.Empty;
        return new ProgrammeSyncResult(true, $"{source.DisplayName} published: {outcome.Summary}{hint}");
    }

    /// <summary>
    /// New data can raise or clear an alert, so detection runs here. The sync
    /// has already succeeded and been published, so a detection failure is
    /// logged rather than reported as a failed sync. The next sync, or
    /// "Refresh alerts", tries again.
    /// </summary>
    private async Task DetectAlertsAsync(ISyncSource source, Guid tenantId)
    {
        try
        {
            await alertDetectionService.DetectForTenantAsync(tenantId, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Alert detection after {Source} sync failed for tenant {TenantId}", source.Name, tenantId);
        }
    }
}
