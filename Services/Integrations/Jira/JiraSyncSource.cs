using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Integrations.OAuth;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.Jira;

public sealed class JiraSyncSource(
    JiraApiClient api,
    ConnectorAccessTokenService tokens,
    IRawConnectorPayloadRepository raw,
    IProgrammeRepository programmes,
    IStaffIdentityResolver identities,
    IAuditLogRepository audit,
    SyncRunCoordinator runs,
    IOptions<ProgrammeOpsOptions> options,
    TimeProvider clock) : ISyncSource
{
    public string Name => "Jira";
    public string DisplayName => "Jira Cloud";
    public string FeatureKey => ProductFeature.JiraSync;
    public SourceCapabilities Capabilities => SourceCapabilities.Work;

    public async Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var credential = await tokens.GetAsync(tenantId, Name, cancellationToken);
        if (!Guid.TryParse(credential.WorkspaceId, out var cloudId) || cloudId == Guid.Empty
            || credential.ProjectIds is null)
            throw new InvalidOperationException("Jira connection is missing its site or project selection. Reconnect Jira.");
        if (credential.ProjectIds.Length == 0)
            return await CompleteIdentityOnlyAsync(tenantId, cloudId, triggeredByMemberId);
        var projectIds = credential.ProjectIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => long.TryParse(value, out var id) ? id : 0).ToArray();
        if (projectIds.Length == 0 || projectIds.Any(id => id <= 0))
            throw new InvalidOperationException("Jira project selection is invalid. Reconnect Jira.");

        await using var run = await runs.TryBeginAsync(tenantId, Name, triggeredByMemberId)
            ?? throw new InvalidOperationException("A Jira sync is already running for this tenant.");
        try
        {
            await run.ReportStageAsync("fetching selected Jira issues");
            var issues = await api.GetIssuesAsync(cloudId, projectIds, credential.ApiToken, cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var programme = await programmes.UpsertProgrammeAsync(new Programme
            {
                ProgrammeKey = Guid.NewGuid(), Name = $"Jira {credential.SiteUrl}", ExternalSource = Name,
                ExternalId = cloudId.ToString("D"), CreatedAtUtc = now, UpdatedAtUtc = now
            }, tenantId);
            var projects = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var workstreams = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var imported = 0;
            foreach (var issue in issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var issueId = ConnectorJson.String(issue, "id");
                var fields = ConnectorJson.Object(issue, "fields");
                var sourceProject = ConnectorJson.Object(fields, "project");
                var projectId = ConnectorJson.String(sourceProject, "id");
                if (string.IsNullOrWhiteSpace(issueId) || string.IsNullOrWhiteSpace(projectId)
                    || !long.TryParse(projectId, out var numericProject) || !projectIds.Contains(numericProject))
                    throw new JsonException("Jira returned an issue outside the selected projects or without a stable ID.");
                await run.TouchAsync();
                await raw.SaveAsync(tenantId, Name, cloudId.ToString("D"), "issue", issueId, issue.GetRawText(), now);
                if (!projects.TryGetValue(projectId, out var projectKey))
                {
                    await run.TouchAsync();
                    var project = await programmes.UpsertProjectAsync(new Project
                    {
                        ProjectKey = Guid.NewGuid(), ProgrammeKey = programme.ProgrammeKey,
                        Name = ConnectorJson.String(sourceProject, "name") ?? projectId,
                        ExternalSource = Name, ExternalId = $"{cloudId:D}:{projectId}",
                        CreatedAtUtc = now, UpdatedAtUtc = now
                    }, tenantId);
                    projects[projectId] = projectKey = project.ProjectKey;
                }
                if (!workstreams.TryGetValue(projectId, out var workstreamKey))
                {
                    await run.TouchAsync();
                    var workstream = await programmes.UpsertWorkstreamAsync(new Workstream
                    {
                        WorkstreamKey = Guid.NewGuid(), ProjectKey = projectKey, Name = "Issues",
                        ExternalSource = Name, ExternalId = $"{cloudId:D}:{projectId}:issues",
                        CreatedAtUtc = now, UpdatedAtUtc = now
                    }, tenantId);
                    workstreams[projectId] = workstreamKey = workstream.WorkstreamKey;
                }
                var status = ConnectorJson.Object(fields, "status");
                var rawStatus = ConnectorJson.String(status, "name");
                var category = ConnectorJson.String(ConnectorJson.Object(status, "statusCategory"), "key");
                var stage = rawStatus?.Contains("block", StringComparison.OrdinalIgnoreCase) == true
                    ? WorkItemLifecycleStage.Blocked : category switch
                    {
                        "done" => WorkItemLifecycleStage.Done,
                        "indeterminate" => WorkItemLifecycleStage.InProgress,
                        "new" => WorkItemLifecycleStage.Backlog,
                        _ => WorkItemLifecycleStage.Unmapped
                    };
                var assignee = ConnectorJson.Object(fields, "assignee");
                var accountId = ConnectorJson.String(assignee, "accountId");
                var staffKey = accountId is null ? null : await identities.ResolveAsync(
                    Name, $"{cloudId:D}:{accountId}", null,
                    ConnectorJson.String(assignee, "displayName"), "Jira assignee", tenantId);
                var due = DateTime.TryParse(ConnectorJson.String(fields, "duedate"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dueDate) ? dueDate : (DateTime?)null;
                var estimate = ConnectorJson.Decimal(fields, "timeoriginalestimate");
                await run.TouchAsync();
                var item = await programmes.UpsertWorkItemAsync(new WorkItem
                {
                    WorkItemKey = Guid.NewGuid(), WorkstreamKey = workstreamKey,
                    Title = ConnectorJson.String(fields, "summary") ?? issueId,
                    Stage = stage, RawStatus = rawStatus,
                    IsMilestone = ConnectorJson.String(ConnectorJson.Object(fields, "issuetype"), "name")
                        ?.Equals("Milestone", StringComparison.OrdinalIgnoreCase) == true,
                    AssignedStaffKey = staffKey, DueDateUtc = due,
                    EstimatedHours = estimate is >= 0 ? estimate / 3600m : null,
                    ExternalSource = Name, ExternalId = $"{cloudId:D}:{issueId}",
                    ParentExternalId = ConnectorJson.String(ConnectorJson.Object(fields, "parent"), "id") is { } parentId
                        ? $"{cloudId:D}:{parentId}" : null,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                }, tenantId);
                await programmes.UpsertWorkItemAllocationsAsync(item.WorkItemKey,
                    staffKey is { } key ? [key] : [], now, tenantId);
                imported++;
            }
            var summary = $"Jira: {imported} issues across {projects.Count} projects; {identities.UnresolvedCount} unresolved people.";
            await run.CompleteAsync(summary, new { issues = imported, projects = projects.Count, unresolvedPeople = identities.UnresolvedCount });
            await audit.LogAsync("JiraSync", cloudId.ToString("D"), "SyncCompleted", triggeredByMemberId,
                JsonSerializer.Serialize(new { issues = imported, projects = projects.Count, runKey = run.Run.RunKey }), now, tenantId);
            if (options.Value.RawPayloadRetentionDays > 0)
                await raw.DeleteOlderThanAsync(now.AddDays(-options.Value.RawPayloadRetentionDays));
            return new SyncOutcome(summary, identities.UnresolvedCount);
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await audit.LogAsync("JiraSync", cloudId.ToString("D"), "SyncFailed", triggeredByMemberId,
                JsonSerializer.Serialize(new { error = ex.GetType().Name, stage = run.Stage, runKey = run.Run.RunKey }),
                clock.GetUtcNow().UtcDateTime, tenantId);
            throw;
        }
    }

    /// <summary>
    /// An identity-only connection (no projects selected) exists so Tempo can
    /// be used without Jira issues: it supplies the site and the people, and
    /// imports nothing. Its sync fetches nothing and publishes a run that says
    /// so, rather than failing, which would read as a broken source.
    /// </summary>
    private async Task<SyncOutcome> CompleteIdentityOnlyAsync(Guid tenantId, Guid cloudId, int? triggeredByMemberId)
    {
        await using var run = await runs.TryBeginAsync(tenantId, Name, triggeredByMemberId)
            ?? throw new InvalidOperationException("A Jira sync is already running for this tenant.");
        const string summary = "Jira: identity-only connection, no projects selected, so no issues were imported.";
        await run.CompleteAsync(summary, new { issues = 0, projects = 0, identityOnly = true });
        await audit.LogAsync("JiraSync", cloudId.ToString("D"), "SyncCompleted", triggeredByMemberId,
            JsonSerializer.Serialize(new { issues = 0, projects = 0, identityOnly = true, runKey = run.Run.RunKey }),
            clock.GetUtcNow().UtcDateTime, tenantId);
        return new SyncOutcome(summary, 0);
    }
}

internal static class ConnectorJson
{
    public static JsonElement Object(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child)
            && child.ValueKind == JsonValueKind.Object ? child : default;
    public static string? String(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child)
            && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
    public static decimal? Decimal(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child)
            && child.ValueKind == JsonValueKind.Number && child.TryGetDecimal(out var number) ? number : null;
}
