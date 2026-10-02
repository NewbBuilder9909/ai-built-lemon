namespace ProgrammePulse.Services.ProgrammeOps;

public interface IJiraTempoReconciliationQueryService
{
    /// <summary>Whether the tenant has any Jira or Tempo data, which is what decides whether the page and its link exist.</summary>
    Task<bool> IsAvailableAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Null when the tenant has no Jira or Tempo data.</summary>
    Task<JiraTempoReconciliationReport?> BuildAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads one tenant's Jira and Tempo facts for <see cref="JiraTempoReconciliationCalculator"/>.
/// Source-aware by name only: it reads Silver through the read repository and
/// never references a vendor namespace. Reachable below Admin, so, like
/// ProgrammeOverviewQueryService, it never touches StaffRate or any cost figure.
/// Time is read for the period only; estimate variance uses the SQL lifetime
/// aggregate rather than the tenant's whole time history.
/// </summary>
public sealed class JiraTempoReconciliationQueryService(
    IProgrammeReadRepository programmes,
    ISyncStatusQueryService syncStatus) : IJiraTempoReconciliationQueryService
{
    public async Task<bool> IsAvailableAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await programmes.GetSourcePresenceAsync(tenantId, JiraTempoReconciliationCalculator.JiraSource, cancellationToken)).Any
        || (await programmes.GetSourcePresenceAsync(tenantId, JiraTempoReconciliationCalculator.TempoSource, cancellationToken)).Any;

    public async Task<JiraTempoReconciliationReport?> BuildAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var jira = await programmes.GetSourcePresenceAsync(tenantId, JiraTempoReconciliationCalculator.JiraSource, cancellationToken);
        var tempo = await programmes.GetSourcePresenceAsync(tenantId, JiraTempoReconciliationCalculator.TempoSource, cancellationToken);
        if (!jira.Any && !tempo.Any)
            return null;

        // A source with no run history for this tenant isn't connected, so it
        // has no run state to report (same rule as EvidenceCheckService).
        var runs = (await syncStatus.GetPublicationStatesAsync(tenantId))
            .Where(s => s.LastPublishedAtUtc is not null || s.LastRunFailed || s.IsRunning)
            .ToDictionary(s => s.Source, s => new SourceRunState(s.DisplayName, s.LastPublishedAtUtc, s.LastRunFailed, s.IsRunning));

        var input = new JiraTempoReconciliationInput(
            from, to, jira, tempo,
            runs.GetValueOrDefault(JiraTempoReconciliationCalculator.JiraSource),
            runs.GetValueOrDefault(JiraTempoReconciliationCalculator.TempoSource),
            jira.HasWorkItems ? await programmes.GetProjectsAsync(tenantId, cancellationToken) : [],
            jira.HasWorkItems ? await programmes.GetWorkstreamsAsync(tenantId, cancellationToken) : [],
            jira.HasWorkItems ? await programmes.GetWorkItemsAsync(tenantId, cancellationToken) : [],
            await programmes.GetTimeEntriesAsync(tenantId, from, to, cancellationToken),
            jira.HasWorkItems ? await programmes.GetLoggedHoursByWorkItemAsync(tenantId, cancellationToken: cancellationToken) : new Dictionary<Guid, decimal>());
        return JiraTempoReconciliationCalculator.Evaluate(input);
    }
}
