using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Integrations.Abstractions;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class SyncStatusQueryService(
    ISyncRunRepository runs,
    ISyncSourceRegistry sourceRegistry,
    TimeProvider timeProvider,
    IEnumerable<UploadSource>? uploadSources = null) : ISyncStatusQueryService
{
    // Sync sources first, then upload sources (file import). Both publish
    // SyncRun rows under their Name, so one code path reports both.
    private IEnumerable<(string Name, string DisplayName)> AllSources =>
        sourceRegistry.All.Select(s => (s.Name, s.DisplayName))
            .Concat((uploadSources ?? []).Select(s => (s.Name, s.DisplayName)));

    public async Task<IReadOnlyList<SourcePublicationStateViewModel>> GetPublicationStatesAsync(Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var states = new List<SourcePublicationStateViewModel>();

        foreach (var source in AllSources)
        {
            var latest = await runs.GetLatestAsync(tenantId, source.Name);
            var latestSuccess = latest?.Status == SyncRunStatus.Succeeded ? latest : await runs.GetLatestSuccessfulAsync(tenantId, source.Name);
            states.Add(Build(source.Name, source.DisplayName, latest, latestSuccess, now));
        }

        return states;
    }

    public async Task<IReadOnlyList<SourcePublicationStateViewModel>> GetPublicationStatesAcrossTenantsAsync()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var states = new List<SourcePublicationStateViewModel>();

        foreach (var source in AllSources)
        {
            var latest = await runs.GetLatestAcrossTenantsAsync(source.Name);
            var latestSuccess = latest?.Status == SyncRunStatus.Succeeded ? latest : await runs.GetLatestSuccessfulAcrossTenantsAsync(source.Name);
            states.Add(Build(source.Name, source.DisplayName, latest, latestSuccess, now));
        }

        return states;
    }

    public static SourcePublicationStateViewModel Build(string source, string displayName, SyncRun? latest, SyncRun? latestSuccess, DateTime nowUtc)
    {
        var isRunning = latest?.Status == SyncRunStatus.Running;
        var lastFailed = latest?.Status == SyncRunStatus.Failed;

        return new SourcePublicationStateViewModel(
            source,
            displayName,
            latestSuccess?.FinishedAtUtc,
            latestSuccess?.Summary,
            isRunning,
            isRunning ? latest!.Stage : null,
            isRunning ? latest!.StartedAtUtc : null,
            lastFailed,
            lastFailed ? latest!.FinishedAtUtc : null,
            lastFailed ? latest!.Stage : null,
            lastFailed ? latest!.Error : null,
            FreshnessLabel(latestSuccess?.FinishedAtUtc, nowUtc));
    }

    public static string FreshnessLabel(DateTime? publishedAtUtc, DateTime nowUtc)
    {
        if (publishedAtUtc is null)
        {
            return "never";
        }

        var age = nowUtc - publishedAtUtc.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} d ago";
    }
}
