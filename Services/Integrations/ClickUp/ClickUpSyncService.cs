using ProgrammePulse.Services.Integrations.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.ClickUp.Raw;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// Orchestrates a ClickUp sync. Operational contract (docs/programme-ops.md):
/// <list type="bullet">
/// <item>One run at a time per (tenant, source), across every application
/// instance: SyncRunCoordinator takes the in-process latch and then the
/// database lease (ProgrammeOps_SyncLease); a second trigger while one is in
/// flight fails fast with a clear message instead of racing the first run's
/// upserts. Two different tenants' ClickUp syncs never serialize against
/// each other.</item>
/// <item>Durable run state: a ProgrammeOps_SyncRun row is Running with a
/// heartbeat/stage while the run is in flight, then Succeeded with a
/// summary or Failed with the error — what the Programme Overview header
/// shows as "last published / running / failed".</item>
/// <item>Idempotent: Silver rows are upserted by (TenantId, ExternalSource,
/// ExternalId), so re-running after a failure converges rather than
/// duplicating, and the same ClickUp id in two tenants never collides.</item>
/// <item>Every outcome is also audited: "SyncCompleted" with counts, or
/// "SyncFailed" with the error and the counts reached so far. A failure
/// leaves Silver consistent-but-partial (each upsert is its own transaction;
/// older rows are merely stale until the next successful run).</item>
/// <item>People are resolved through IStaffIdentityResolver; anyone it
/// can't match is on the identity queue and counted in the result.</item>
/// <item>Transient upstream failures are retried inside the HttpClient
/// pipeline (TransientHttpRetryHandler) before they ever reach here.</item>
/// <item>After a successful run, Bronze captures and stale unresolved
/// identities older than ProgrammeOpsOptions.RawPayloadRetentionDays, and
/// finished sync runs older than SyncRunHistoryRetentionDays, are purged.</item>
/// </list>
/// A tenant that has configured its own ClickUp connection (workspace +
/// token, set at /staffops/programme/connections and encrypted via
/// ISourceCredentialProtector) syncs that account; a tenant that hasn't
/// falls back to the deployment-wide ClickUp:WorkspaceId/ClickUp:ApiToken —
/// see docs/tenancy.md and docs/programme-ops.md's "Tenant-owned source
/// connections" section.
/// </summary>
public sealed class ClickUpSyncService(
    IClickUpApiClient apiClient,
    IClickUpRawPayloadRepository rawPayloadRepository,
    IClickUpMappingService mappingService,
    IStaffIdentityResolver identityResolver,
    IIdentityResolutionRepository identityRepository,
    IAuditLogRepository auditLogRepository,
    ISyncRunRepository syncRunRepository,
    ISourceConnectionRepository sourceConnectionRepository,
    ISourceCredentialProtector credentialProtector,
    IOptions<ClickUpOptions> options,
    IOptions<ProgrammeOpsOptions> programmeOpsOptions,
    SyncRunCoordinator syncRunCoordinator,
    TimeProvider timeProvider,
    IHostEnvironment environment) : IClickUpSyncService
{
    public const string SourceName = "ClickUp";
    public const string AuditEntityType = "ClickUpSync";

    public async Task<ClickUpSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var existingConnection = await sourceConnectionRepository.GetActiveForTenantAsync(tenantId, SourceName);
        var credential = credentialProtector.Unprotect(existingConnection?.ProtectedCredentialJson);

        if (existingConnection?.ProtectedCredentialJson is not null && credential is null)
            throw new InvalidOperationException("This tenant's ClickUp credential cannot be decrypted. Reconnect ClickUp before syncing.");
        if (credential is null && !(environment.IsDevelopment() && programmeOpsOptions.Value.AllowSharedSourceCredentials))
            throw new InvalidOperationException("Connect this tenant's own ClickUp account before syncing.");

        var workspaceId = credential is null ? options.Value.WorkspaceId : credential.WorkspaceId;
        var apiToken = credential is null ? options.Value.ApiToken : credential.ApiToken;
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException(
                "ClickUp workspace and token are required. Configure this tenant's connection before syncing.");
        }

        var tenantClient = apiClient.WithCredential(apiToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Auto-provisions this tenant's ClickUp SourceConnection row on its
        // first sync, and refreshes ExternalAccountId to whichever
        // workspace was actually used — see
        // Models/Programme/SourceConnection's doc comment.
        await sourceConnectionRepository.GetOrCreateActiveAsync(tenantId, SourceName, workspaceId, now);

        await using var run = await syncRunCoordinator.TryBeginAsync(tenantId, SourceName, triggeredByMemberId)
            ?? throw new InvalidOperationException("A ClickUp sync is already running (on this or another instance). Wait for it to finish before starting another.");

        var progress = new SyncProgress();

        ClickUpSyncResult result;
        try
        {
            result = await RunCoreAsync(tenantClient, workspaceId, tenantId, now, progress, run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SyncLeaseLostException ex)
        {
            // Another instance owns the source now and has already marked
            // this run abandoned; FailAsync below is a status-guarded no-op
            // for the run row, but the audit trail still needs the event.
            await run.FailAsync(ex);
            await auditLogRepository.LogAsync(
                AuditEntityType,
                workspaceId,
                "SyncLeaseLost",
                triggeredByMemberId,
                JsonSerializer.Serialize(new { reachedStage = progress.Stage, progress.Programmes, progress.Projects, progress.Workstreams, progress.WorkItems, progress.TimeEntries, runKey = run.Run.RunKey }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);

            throw new InvalidOperationException($"ClickUp sync stopped while {progress.Stage}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await auditLogRepository.LogAsync(
                AuditEntityType,
                workspaceId,
                "SyncFailed",
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    error = ex.GetType().Name,
                    message = ex.Message,
                    reachedStage = progress.Stage,
                    progress.Programmes,
                    progress.Projects,
                    progress.Workstreams,
                    progress.WorkItems,
                    progress.TimeEntries,
                    runKey = run.Run.RunKey
                }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);

            throw new InvalidOperationException(
                $"ClickUp sync failed while {progress.Stage}: {ex.Message} Re-run the sync once the cause is fixed — completed work is kept and re-running is safe.", ex);
        }

        await run.CompleteAsync(result.Describe(), result);
        await auditLogRepository.LogAsync(
            AuditEntityType,
            workspaceId,
            "SyncCompleted",
            triggeredByMemberId,
            JsonSerializer.Serialize(new { result.Programmes, result.Projects, result.Workstreams, result.WorkItems, result.TimeEntries, result.UnresolvedPeople, runKey = run.Run.RunKey }),
            timeProvider.GetUtcNow().UtcDateTime,
            tenantId);

        await PurgeExpiredAsync(workspaceId, triggeredByMemberId, now, tenantId);

        return result;
    }

    private async Task<ClickUpSyncResult> RunCoreAsync(IClickUpApiClient apiClient, string workspaceId, Guid tenantId, DateTime now, SyncProgress progress, SyncRunHandle run, CancellationToken cancellationToken)
    {
        await StageAsync(progress, run, "fetching spaces");
        var spaces = await apiClient.GetSpacesAsync(workspaceId, cancellationToken);
        foreach (var spaceEntity in spaces)
        {
            await StageAsync(progress, run, $"syncing space '{spaceEntity.Item.Name}'");
            await CapturedAsync("space", spaceEntity.Item.Id, workspaceId, spaceEntity.RawJson, now, tenantId);
            await run.TouchAsync();
            var programme = await mappingService.MapProgrammeAsync(spaceEntity.Item, tenantId);
            progress.Programmes++;

            var folders = await apiClient.GetFoldersAsync(spaceEntity.Item.Id, cancellationToken);
            foreach (var folderEntity in folders)
            {
                await CapturedAsync("folder", folderEntity.Item.Id, workspaceId, folderEntity.RawJson, now, tenantId);
                await run.TouchAsync();
                var project = await mappingService.MapProjectAsync(folderEntity.Item, programme.ProgrammeKey, tenantId);
                progress.Projects++;

                var lists = await apiClient.GetListsAsync(folderEntity.Item.Id, cancellationToken);
                await SyncListsAsync(apiClient, lists, project.ProjectKey, workspaceId, tenantId, now, progress, run, cancellationToken);
            }

            // Folderless lists sit directly under the space with no folder to
            // group them — represented as a single synthetic "Unsorted"
            // Project per space so they still fit the Programme/Project/
            // Workstream hierarchy instead of being dropped.
            var folderlessLists = await apiClient.GetFolderlessListsAsync(spaceEntity.Item.Id, cancellationToken);
            if (folderlessLists.Count > 0)
            {
                var unsortedFolder = new ClickUpFolderDto { Id = $"{spaceEntity.Item.Id}-unsorted", Name = "Unsorted" };
                await run.TouchAsync();
                var unsortedProject = await mappingService.MapProjectAsync(unsortedFolder, programme.ProgrammeKey, tenantId);
                progress.Projects++;

                await SyncListsAsync(apiClient, folderlessLists, unsortedProject.ProjectKey, workspaceId, tenantId, now, progress, run, cancellationToken);
            }
        }

        // Workspace members are captured to Bronze only — there's no Silver
        // "member" entity, since staff identity already lives in the Staff
        // domain and is resolved per assignee (see IStaffIdentityResolver).
        // Time entries below are mapped into Silver (ProgrammeOps_TimeEntry).
        await StageAsync(progress, run, "fetching workspace members");
        var members = await apiClient.GetWorkspaceMembersAsync(workspaceId, cancellationToken);
        foreach (var member in members)
        {
            await CapturedAsync("member", member.Item.Id.ToString(), workspaceId, member.RawJson, now, tenantId);
        }

        await StageAsync(progress, run, "syncing time entries");
        var timeEntries = await apiClient.GetTimeEntriesAsync(workspaceId, cancellationToken);
        foreach (var batch in timeEntries.Chunk(WriteBatchSize))
        {
            await CapturedAsync("timeentry", batch.Select(t => (t.Item.Id, t.RawJson)).ToList(), workspaceId, now, tenantId);
            await run.TouchAsync();
            await mappingService.MapTimeEntriesAsync(batch.Select(t => t.Item).ToList(), tenantId);
            progress.TimeEntries += batch.Length;
        }

        return new ClickUpSyncResult(spaces.Count, progress.Projects, progress.Workstreams, progress.WorkItems, progress.TimeEntries,
            identityResolver.UnresolvedCount, identityResolver.AmbiguousCount, identityResolver.UnidentifiableSightings);
    }

    private async Task SyncListsAsync(
        IClickUpApiClient apiClient,
        IReadOnlyList<RawEntity<ClickUpListDto>> lists,
        Guid projectKey,
        string workspaceId,
        Guid tenantId,
        DateTime capturedAtUtc,
        SyncProgress progress,
        SyncRunHandle run,
        CancellationToken cancellationToken)
    {
        foreach (var listEntity in lists)
        {
            await CapturedAsync("list", listEntity.Item.Id, workspaceId, listEntity.RawJson, capturedAtUtc, tenantId);
            await run.TouchAsync();
            var workstream = await mappingService.MapWorkstreamAsync(listEntity.Item, projectKey, tenantId);
            progress.Workstreams++;

            // Touch before every Silver write, including inside the task
            // loop (a big list can take minutes): the lease is re-checked
            // before a stalled worker can write over the new owner's rows.
            // Tasks are written a batch at a time (finding A3), so the touch
            // precedes each batch write rather than each row.
            var tasks = await apiClient.GetTasksAsync(listEntity.Item.Id, cancellationToken);
            foreach (var batch in tasks.Chunk(WriteBatchSize))
            {
                await CapturedAsync("task", batch.Select(t => (t.Item.Id, t.RawJson)).ToList(), workspaceId, capturedAtUtc, tenantId);
                await run.TouchAsync();
                await mappingService.MapWorkItemsAsync(batch.Select(t => t.Item).ToList(), workstream.WorkstreamKey, tenantId);
                progress.WorkItems += batch.Length;
            }
        }
    }

    private static async Task StageAsync(SyncProgress progress, SyncRunHandle run, string stage)
    {
        progress.Stage = stage;
        await run.ReportStageAsync(stage);
    }

    /// <summary>
    /// Runs after "SyncCompleted" has been written, in its own try/catch: a
    /// housekeeping failure must not turn a successful sync into a reported
    /// failure, but it must not be silent either.
    /// </summary>
    private async Task PurgeExpiredAsync(string workspaceId, int? triggeredByMemberId, DateTime now, Guid tenantId)
    {
        var retention = programmeOpsOptions.Value;
        try
        {
            if (retention.RawPayloadRetentionDays > 0)
            {
                var cutoff = now.AddDays(-retention.RawPayloadRetentionDays);
                await rawPayloadRepository.DeleteOlderThanAsync(cutoff);
                await identityRepository.DeleteUnresolvedNotSeenSinceAsync(cutoff);
            }

            if (retention.SyncRunHistoryRetentionDays > 0)
            {
                await syncRunRepository.DeleteFinishedOlderThanAsync(now.AddDays(-retention.SyncRunHistoryRetentionDays));
            }
        }
        catch (Exception ex)
        {
            await auditLogRepository.LogAsync(
                AuditEntityType,
                workspaceId,
                "RetentionPurgeFailed",
                triggeredByMemberId,
                JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message, retention.RawPayloadRetentionDays, retention.SyncRunHistoryRetentionDays }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);
        }
    }

    private Task CapturedAsync(string entityType, string externalId, string workspaceId, string rawJson, DateTime fetchedAtUtc, Guid tenantId) =>
        rawPayloadRepository.SaveAsync(entityType, externalId, workspaceId, rawJson, fetchedAtUtc, tenantId);

    private Task CapturedAsync(string entityType, IReadOnlyList<(string ExternalId, string RawJson)> payloads, string workspaceId, DateTime fetchedAtUtc, Guid tenantId) =>
        rawPayloadRepository.SaveManyAsync(entityType, payloads, workspaceId, fetchedAtUtc, tenantId);

    /// <summary>
    /// Rows per batched Bronze capture and Silver write. Large enough that a
    /// task or time entry costs well under one round trip; small enough that
    /// a batch's transaction and statement stay modest.
    /// </summary>
    private const int WriteBatchSize = 200;

    private sealed class SyncProgress
    {
        public string Stage { get; set; } = "starting";
        public int Programmes { get; set; }
        public int Projects { get; set; }
        public int Workstreams { get; set; }
        public int WorkItems { get; set; }
        public int TimeEntries { get; set; }
    }
}
