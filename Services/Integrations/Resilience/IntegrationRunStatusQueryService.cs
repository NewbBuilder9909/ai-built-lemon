using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.Resilience;

/// <summary>
/// Reads durable sync-run state for the non-ProgrammeOps connectors that now
/// share the same source-neutral lease/run infrastructure. The caller supplies
/// the small source list it wants, so tenant-facing pages do not inherit the
/// Programme Overview's registry of delivery sources.
/// </summary>
public interface IIntegrationRunStatusQueryService
{
    Task<IReadOnlyList<SourcePublicationStateViewModel>> GetStatesAsync(
        Guid tenantId,
        IReadOnlyList<(string Source, string DisplayName)> sources);
}

public sealed class IntegrationRunStatusQueryService(
    ISyncRunRepository runs,
    TimeProvider timeProvider) : IIntegrationRunStatusQueryService
{
    public async Task<IReadOnlyList<SourcePublicationStateViewModel>> GetStatesAsync(
        Guid tenantId,
        IReadOnlyList<(string Source, string DisplayName)> sources)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var states = new List<SourcePublicationStateViewModel>(sources.Count);

        foreach (var (source, displayName) in sources)
        {
            var latest = await runs.GetLatestAsync(tenantId, source);
            var latestSuccess = latest?.Status == Models.Programme.SyncRunStatus.Succeeded
                ? latest
                : await runs.GetLatestSuccessfulAsync(tenantId, source);
            states.Add(SyncStatusQueryService.Build(source, displayName, latest, latestSuccess, now));
        }

        return states;
    }
}
