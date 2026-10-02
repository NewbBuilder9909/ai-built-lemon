using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// Orchestrates a Hub Planner sync. Same operational contract as
/// ClickUpSyncService (one run at a time per (tenant, source) via the
/// database lease, idempotent upserts, durable run state, every outcome
/// audited as SyncCompleted/SyncFailed, transient HTTP failures retried in
/// the client pipeline, retention purges after success) — see that class's
/// doc comment and docs/programme-ops.md.
///
/// Bookings become PlannedAllocation rows, never WorkItems (see
/// Models/Programme/PlannedAllocation). Resources are resolved through
/// IStaffIdentityResolver, so a resource nobody can match lands on the
/// identity queue instead of vanishing.
///
/// A tenant that has configured its own Hub Planner connection (an API key,
/// set at /staffops/programme/connections and encrypted via
/// ISourceCredentialProtector) syncs that account; a tenant that hasn't
/// falls back to the deployment-wide HubPlanner:ApiKey.
/// </summary>
public sealed class HubPlannerSyncService(
    IHubPlannerApiClient apiClient,
    IHubPlannerRawPayloadRepository rawPayloadRepository,
    IHubPlannerMappingService mappingService,
    IStaffIdentityResolver identityResolver,
    IIdentityResolutionRepository identityRepository,
    IAuditLogRepository auditLogRepository,
    ISyncRunRepository syncRunRepository,
    ISourceConnectionRepository sourceConnectionRepository,
    ISourceCredentialProtector credentialProtector,
    IOptions<HubPlannerOptions> hubPlannerOptions,
    IOptions<ProgrammeOpsOptions> programmeOpsOptions,
    SyncRunCoordinator syncRunCoordinator,
    TimeProvider timeProvider,
    IHostEnvironment environment) : IHubPlannerSyncService
{
    public const string SourceName = "HubPlanner";
    public const string AuditEntityType = "HubPlannerSync";
    private const string AuditEntityId = "hubplanner";
    private const string ResourceContext = "booking resource";

    public async Task<HubPlannerSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var existingConnection = await sourceConnectionRepository.GetActiveForTenantAsync(tenantId, SourceName);
        var credential = credentialProtector.Unprotect(existingConnection?.ProtectedCredentialJson);
        if (existingConnection?.ProtectedCredentialJson is not null && credential is null)
            throw new InvalidOperationException("This tenant's Hub Planner credential cannot be decrypted. Reconnect Hub Planner before syncing.");
        if (credential is null && !(environment.IsDevelopment() && programmeOpsOptions.Value.AllowSharedSourceCredentials))
            throw new InvalidOperationException("Connect this tenant's own Hub Planner account before syncing.");
        var apiKey = credential is null ? hubPlannerOptions.Value.ApiKey : credential.ApiToken;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Hub Planner API key is required. Configure this tenant's connection before syncing.");
        var tenantClient = apiClient.WithCredential(apiKey);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Auto-provisions this tenant's Hub Planner SourceConnection row on
        // its first sync — see ClickUpSyncService.RunAsync and
        // Models/Programme/SourceConnection's doc comment.
        await sourceConnectionRepository.GetOrCreateActiveAsync(tenantId, SourceName, externalAccountId: null, now);

        await using var run = await syncRunCoordinator.TryBeginAsync(tenantId, SourceName, triggeredByMemberId)
            ?? throw new InvalidOperationException("A Hub Planner sync is already running (on this or another instance). Wait for it to finish before starting another.");

        var progress = new SyncProgress();

        HubPlannerSyncResult result;
        try
        {
            result = await RunCoreAsync(tenantClient, tenantId, now, progress, run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SyncLeaseLostException ex)
        {
            // Another instance owns the source now and has already marked
            // this run abandoned; FailAsync is a status-guarded no-op for
            // the run row, but the audit trail still needs the event.
            await run.FailAsync(ex);
            await auditLogRepository.LogAsync(
                AuditEntityType,
                AuditEntityId,
                "SyncLeaseLost",
                triggeredByMemberId,
                JsonSerializer.Serialize(new { reachedStage = progress.Stage, progress.Projects, progress.PlannedAllocations, runKey = run.Run.RunKey }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);

            throw new InvalidOperationException($"Hub Planner sync stopped while {progress.Stage}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await auditLogRepository.LogAsync(
                AuditEntityType,
                AuditEntityId,
                "SyncFailed",
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    error = ex.GetType().Name,
                    message = ex.Message,
                    reachedStage = progress.Stage,
                    progress.Projects,
                    progress.PlannedAllocations,
                    runKey = run.Run.RunKey
                }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);

            throw new InvalidOperationException(
                $"Hub Planner sync failed while {progress.Stage}: {ex.Message} Re-run the sync once the cause is fixed — completed work is kept and re-running is safe.", ex);
        }

        await run.CompleteAsync(result.Describe(), result);
        await auditLogRepository.LogAsync(
            AuditEntityType,
            AuditEntityId,
            "SyncCompleted",
            triggeredByMemberId,
            JsonSerializer.Serialize(new { result.Projects, result.PlannedAllocations, result.UnresolvedResources, runKey = run.Run.RunKey }),
            timeProvider.GetUtcNow().UtcDateTime,
            tenantId);

        await PurgeExpiredAsync(triggeredByMemberId, now, tenantId);

        return result;
    }

    private async Task<HubPlannerSyncResult> RunCoreAsync(IHubPlannerApiClient apiClient, Guid tenantId, DateTime now, SyncProgress progress, SyncRunHandle run, CancellationToken cancellationToken)
    {
        // Clients are captured to Bronze only — never mapped to Silver
        // Customer, which is deliberately admin-authored, not synced (see
        // Models/Programme/Customer.cs and HubPlannerClientDto's doc comment).
        await StageAsync(progress, run, "fetching clients");
        var clients = await apiClient.GetClientsAsync(cancellationToken);
        foreach (var clientEntity in clients)
        {
            await CapturedAsync("client", clientEntity.Item.Id, clientEntity.RawJson, now, tenantId);
        }

        // Resources are captured to Bronze only, same as ClickUp workspace
        // members — resolved to a StaffProfile per booking below.
        await StageAsync(progress, run, "fetching resources");
        var resources = await apiClient.GetResourcesAsync(cancellationToken);
        var resourcesById = new Dictionary<string, HubPlannerResourceDto>();
        foreach (var resourceEntity in resources)
        {
            await CapturedAsync("resource", resourceEntity.Item.Id, resourceEntity.RawJson, now, tenantId);
            resourcesById[resourceEntity.Item.Id] = resourceEntity.Item;
        }

        var programme = await mappingService.MapRootProgrammeAsync(tenantId);

        await StageAsync(progress, run, "syncing projects");
        var projects = await apiClient.GetProjectsAsync(cancellationToken);
        var projectKeysByExternalId = new Dictionary<string, Guid>();
        foreach (var projectEntity in projects)
        {
            await CapturedAsync("project", projectEntity.Item.Id, projectEntity.RawJson, now, tenantId);
            await run.TouchAsync();
            var project = await mappingService.MapProjectAsync(projectEntity.Item, programme.ProgrammeKey, tenantId);
            progress.Projects++;
            projectKeysByExternalId[projectEntity.Item.Id] = project.ProjectKey;
        }

        await StageAsync(progress, run, "syncing bookings");
        var bookings = await apiClient.GetBookingsAsync(cancellationToken);
        foreach (var bookingEntity in bookings)
        {
            await CapturedAsync("booking", bookingEntity.Item.Id, bookingEntity.RawJson, now, tenantId);

            // A booking against a project this sync didn't just see (e.g. an
            // archived project /project didn't return) has nowhere to live —
            // skip it rather than inventing a home for it.
            if (!projectKeysByExternalId.TryGetValue(bookingEntity.Item.Project, out var projectKey))
            {
                progress.SkippedBookings++;
                continue;
            }

            var resolvedStaffKey = await ResolveResourceAsync(bookingEntity.Item.Resource, resourcesById, tenantId);
            await run.TouchAsync();
            await mappingService.MapBookingAsync(bookingEntity.Item, projectKey, resolvedStaffKey, tenantId);
            progress.PlannedAllocations++;
        }

        return new HubPlannerSyncResult(progress.Projects, progress.PlannedAllocations, identityResolver.UnresolvedCount, progress.SkippedBookings,
            identityResolver.AmbiguousCount, identityResolver.UnidentifiableSightings);
    }

    private Task<Guid?> ResolveResourceAsync(string resourceId, IReadOnlyDictionary<string, HubPlannerResourceDto> resourcesById, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Task.FromResult<Guid?>(null);
        }

        resourcesById.TryGetValue(resourceId, out var resource);
        var displayName = resource is null ? null : $"{resource.FirstName} {resource.LastName}".Trim();
        return identityResolver.ResolveAsync(SourceName, resourceId, resource?.Email, displayName, ResourceContext, tenantId);
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
    private async Task PurgeExpiredAsync(int? triggeredByMemberId, DateTime now, Guid tenantId)
    {
        var options = programmeOpsOptions.Value;
        try
        {
            if (options.RawPayloadRetentionDays > 0)
            {
                var cutoff = now.AddDays(-options.RawPayloadRetentionDays);
                await rawPayloadRepository.DeleteOlderThanAsync(cutoff);
                await identityRepository.DeleteUnresolvedNotSeenSinceAsync(cutoff);
            }

            if (options.SyncRunHistoryRetentionDays > 0)
            {
                await syncRunRepository.DeleteFinishedOlderThanAsync(now.AddDays(-options.SyncRunHistoryRetentionDays));
            }
        }
        catch (Exception ex)
        {
            await auditLogRepository.LogAsync(
                AuditEntityType,
                AuditEntityId,
                "RetentionPurgeFailed",
                triggeredByMemberId,
                JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message, options.RawPayloadRetentionDays, options.SyncRunHistoryRetentionDays }),
                timeProvider.GetUtcNow().UtcDateTime,
                tenantId);
        }
    }

    private Task CapturedAsync(string entityType, string externalId, string rawJson, DateTime fetchedAtUtc, Guid tenantId) =>
        rawPayloadRepository.SaveAsync(entityType, externalId, rawJson, fetchedAtUtc, tenantId);

    private sealed class SyncProgress
    {
        public string Stage { get; set; } = "starting";
        public int Projects { get; set; }
        public int PlannedAllocations { get; set; }
        public int SkippedBookings { get; set; }
    }
}
