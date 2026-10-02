using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>What one Azure DevOps ingestion run did, in terms a reader can act on.</summary>
public sealed record AzureDevOpsIngestionResult(
    int EvidenceRows,
    int StreamsCompleted,
    int StreamsPartial,
    int StreamsPermissionLost,
    int UnmappedActors,
    int BotActorsExcluded,
    string Summary);

/// <summary>
/// Runs one evidence ingestion for one Azure DevOps connection, writing the
/// same provider-neutral <see cref="EngineeringEvidence"/> rows, coverage
/// cursors and mapping queue as the GitHub connector — so the portfolio,
/// continuity view and suggestions read Azure DevOps evidence with no
/// change above Bronze.
///
/// It keeps every rule GitHubEvidenceIngestionService keeps, restated here
/// because the two vendors must not reference each other:
///
/// - **The processing-decision gate runs first.** No current lawful-basis,
///   worker-notice and DPIA decision, no collection.
/// - **No deployment-wide credential.** The tenant's own token or nothing.
/// - **An incomplete run never looks complete.** A failed page, throttling
///   or lost permission keeps what was fetched, leaves the coverage claim
///   where it was and says why. Nothing previously collected is deleted.
/// - **One run per tenant and source at a time**, under its own latch name
///   so it never queues behind a GitHub run.
///
/// One addition: a personal access token has an expiry the admin can record
/// at connect time, and an expired token stops the run with a message that
/// names the date instead of surfacing as a string of permission failures.
/// </summary>
public sealed class AzureDevOpsEvidenceIngestionService(
    IAzureDevOpsEvidenceClient client,
    IEngineeringEvidenceRepository repository,
    IEvidenceCredentialProtector protector,
    IEvidenceActorResolver resolver,
    ISkillsEvidenceAuditLogRepository auditLog,
    IContinuityRepository continuity,
    ISyncRunRepository syncRunRepository,
    IOptions<AzureDevOpsEvidenceOptions> options,
    SyncRunCoordinator syncRunCoordinator,
    TimeProvider timeProvider)
{
    public const string SourceName = "AzureDevOpsEvidence";

    public async Task<AzureDevOpsIngestionResult> RunAsync(
        Guid connectionKey, Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var decision = await continuity.GetLiveProcessingDecisionAsync(tenantId);
        if (decision is null)
        {
            throw new SkillAssertionValidationException(EvidenceProcessingDecision.MissingReason);
        }

        if (decision.BlockingReason(DateOnly.FromDateTime(now)) is { } blocked)
        {
            throw new SkillAssertionValidationException(blocked);
        }

        var connection = await repository.GetConnectionAsync(connectionKey, tenantId)
            ?? throw new Shared.CrossTenantReferenceException("EvidenceConnection", connectionKey);

        if (!string.Equals(connection.Provider, AzureDevOpsEvidenceMapper.ProviderName, StringComparison.Ordinal))
        {
            throw new SkillAssertionValidationException("This is not an Azure DevOps connection.");
        }

        if (connection.Status != EvidenceConnectionStatus.Active)
        {
            throw new SkillAssertionValidationException(
                "This connection is not active. Reconnect it before running an evidence sync.");
        }

        var credential = protector.Unprotect(connection.ProtectedCredentialJson)
            ?? throw new SkillAssertionValidationException(
                "This connection has no readable credential. Reconnect it — there is no shared fallback for evidence sources.");

        if (credential.ExpiresAtUtc is { } expires && expires <= timeProvider.GetUtcNow())
        {
            throw new SkillAssertionValidationException(
                $"The personal access token for this connection expired on {expires:yyyy-MM-dd}. Reconnect with a new token.");
        }

        var mapper = new AzureDevOpsEvidenceMapper(resolver);
        await using var run = await syncRunCoordinator.TryBeginAsync(tenantId, SourceName, triggeredByMemberId)
            ?? throw new SkillAssertionValidationException(
                "An Azure DevOps evidence sync is already running for this tenant (on this or another instance). Wait for it to finish.");

        var rows = 0;
        var complete = 0;
        var partial = 0;
        var permissionLost = 0;

        try
        {
            await run.ReportStageAsync("checking selected repositories");
            await run.TouchAsync();
            await repository.MarkCoverageOutOfScopeAsync(connectionKey, connection.SelectedRepositories, tenantId, now);

            foreach (var repositoryKey in connection.SelectedRepositories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await run.ReportStageAsync($"syncing {repositoryKey}");

                var existing = await repository.GetCoverageForConnectionAsync(connectionKey, tenantId);
                EvidenceCoverage? Cursor(EvidenceStream stream) => existing.FirstOrDefault(c =>
                    string.Equals(c.RepositoryKey, repositoryKey, StringComparison.OrdinalIgnoreCase) && c.Stream == stream);

                var outcomes = new[]
                {
                    await IngestCommitsAsync(connection, credential, repositoryKey, Cursor(EvidenceStream.Commits), mapper, run.Run.RunKey, now, run, cancellationToken),
                    await IngestPullRequestsAsync(connection, credential, repositoryKey,
                        Cursor(EvidenceStream.PullRequests), Cursor(EvidenceStream.Reviews), mapper, run.Run.RunKey, now, run, cancellationToken)
                };

                foreach (var outcome in outcomes)
                {
                    rows += outcome.Rows;
                    switch (outcome.Status)
                    {
                        case EvidenceCoverageStatus.Complete: complete++; break;
                        case EvidenceCoverageStatus.PermissionLost: permissionLost++; break;
                        default: partial++; break;
                    }
                }
            }

            if (permissionLost > 0 && complete == 0)
            {
                await run.ReportStageAsync("marking connection access lost");
                await run.TouchAsync();
                await repository.SetConnectionStatusAsync(
                    connectionKey, EvidenceConnectionStatus.AccessLost, tenantId, now, clearCredential: false);
            }

            var result = new AzureDevOpsIngestionResult(
                rows, complete, partial, permissionLost, resolver.UnmappedCount, resolver.BotCount,
                $"{rows} evidence rows; {complete} streams complete, {partial} partial, {permissionLost} without permission; "
                + $"{resolver.UnmappedCount} unmapped {(resolver.UnmappedCount == 1 ? "account" : "accounts")}, "
                + $"{resolver.BotCount} bot {(resolver.BotCount == 1 ? "account" : "accounts")} excluded");

            await run.CompleteAsync(result.Summary, new
            {
                provider = AzureDevOpsEvidenceMapper.ProviderName,
                rows,
                complete,
                partial,
                permissionLost,
                unmapped = resolver.UnmappedCount,
                ambiguous = resolver.AmbiguousCount,
                bots = resolver.BotCount,
                connectionKey
            });

            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeConnection, connectionKey.ToString(),
                permissionLost > 0 && complete == 0
                    ? SkillsEvidenceAuditAction.EvidenceSyncFailed
                    : SkillsEvidenceAuditAction.EvidenceSyncCompleted,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    provider = AzureDevOpsEvidenceMapper.ProviderName,
                    runKey = run.Run.RunKey,
                    rows,
                    complete,
                    partial,
                    permissionLost,
                    unmapped = resolver.UnmappedCount,
                    ambiguous = resolver.AmbiguousCount,
                    bots = resolver.BotCount
                }),
                now, tenantId);

            await PurgeExpiredAsync(now);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await run.FailAsync(new OperationCanceledException("The Azure DevOps evidence sync request was cancelled before it completed."));
            throw;
        }
        catch (SyncLeaseLostException ex)
        {
            await run.FailAsync(ex);
            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeConnection, connectionKey.ToString(),
                SkillsEvidenceAuditAction.EvidenceSyncFailed,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    provider = AzureDevOpsEvidenceMapper.ProviderName,
                    runKey = run.Run.RunKey,
                    reachedStage = run.Stage,
                    rows,
                    complete,
                    partial,
                    permissionLost,
                    unmapped = resolver.UnmappedCount,
                    ambiguous = resolver.AmbiguousCount,
                    bots = resolver.BotCount
                }),
                timeProvider.GetUtcNow().UtcDateTime, tenantId);

            throw new SkillAssertionValidationException(
                $"The Azure DevOps evidence sync stopped while {run.Stage ?? "running"} because another instance took over. Re-run it once the other run finishes.");
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await auditLog.LogAsync(
                SkillsEvidenceAuditAction.EntityTypeConnection, connectionKey.ToString(),
                SkillsEvidenceAuditAction.EvidenceSyncFailed,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    provider = AzureDevOpsEvidenceMapper.ProviderName,
                    runKey = run.Run.RunKey,
                    error = ex.GetType().Name,
                    message = ex.Message,
                    reachedStage = run.Stage,
                    rows,
                    complete,
                    partial,
                    permissionLost,
                    unmapped = resolver.UnmappedCount,
                    ambiguous = resolver.AmbiguousCount,
                    bots = resolver.BotCount
                }),
                timeProvider.GetUtcNow().UtcDateTime, tenantId);

            throw new SkillAssertionValidationException(
                $"The Azure DevOps evidence sync failed while {run.Stage ?? "running"}: {ex.Message} Re-run it once the cause is fixed — completed work is kept and re-running is safe.");
        }
    }

    private async Task RequireCollectionAsync(Guid tenantId)
    {
        var decision = await continuity.GetLiveProcessingDecisionAsync(tenantId);
        var reason = decision is null ? EvidenceProcessingDecision.MissingReason
            : decision.BlockingReason(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        if (reason is not null) throw new SkillAssertionValidationException(reason);
    }
    private sealed record StreamOutcome(int Rows, EvidenceCoverageStatus Status);

    private async Task<StreamOutcome> IngestCommitsAsync(
        EvidenceConnection connection, EvidenceCredential credential, string repositoryKey,
        EvidenceCoverage? existing, AzureDevOpsEvidenceMapper mapper, Guid runKey, DateTime now, SyncRunHandle run, CancellationToken cancellationToken)
    {
        AzureDevOpsPage<AzureDevOpsCommit> page;
        try
        {
            await RequireCollectionAsync(connection.TenantId);
            page = await client.GetDefaultBranchCommitsAsync(
                connection.ApiBaseUrl, credential.AccessToken, repositoryKey, existing?.Cursor, cancellationToken);
        }
        catch (AzureDevOpsAccessLostException ex)
        {
            await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Commits, existing,
                EvidenceCoverageStatus.PermissionLost, ex.Message, existing?.Cursor, runKey, now, advanceClaim: false);
            return new StreamOutcome(0, EvidenceCoverageStatus.PermissionLost);
        }

        var rows = 0;
        foreach (var commit in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RequireCollectionAsync(connection.TenantId);

            await run.TouchAsync();
            await repository.SaveRawAsync(
                connection.TenantId, connection.ConnectionKey, AzureDevOpsEvidenceMapper.ProviderName,
                connection.SourceAccountId, "commit", commit.CommitId, JsonSerializer.Serialize(commit), now);

            foreach (var evidence in await mapper.MapCommitAsync(commit, connection, repositoryKey, runKey, now))
            {
                await RequireCollectionAsync(connection.TenantId);
                await repository.UpsertEvidenceAsync(evidence);
                rows++;
            }
        }

        var status = page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial;
        await run.TouchAsync();
        await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Commits, existing,
            status, page.IncompleteReason, page.NextCursor, runKey, now, advanceClaim: page.IsComplete);

        return new StreamOutcome(rows, status);
    }

    /// <summary>
    /// Pull requests and their reviews arrive in one response, so the
    /// review stream's coverage is exactly the pull-request page's: a
    /// partial page is a partial review set too, recorded as such rather
    /// than folded into the pull-request row.
    /// </summary>
    private async Task<StreamOutcome> IngestPullRequestsAsync(
        EvidenceConnection connection, EvidenceCredential credential, string repositoryKey,
        EvidenceCoverage? existing, EvidenceCoverage? existingReviews, AzureDevOpsEvidenceMapper mapper,
        Guid runKey, DateTime now, SyncRunHandle run, CancellationToken cancellationToken)
    {
        AzureDevOpsPage<AzureDevOpsPullRequest> page;
        try
        {
            await RequireCollectionAsync(connection.TenantId);
            page = await client.GetCompletedPullRequestsAsync(
                connection.ApiBaseUrl, credential.AccessToken, repositoryKey, existing?.Cursor, cancellationToken);
        }
        catch (AzureDevOpsAccessLostException ex)
        {
            await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.PullRequests, existing,
                EvidenceCoverageStatus.PermissionLost, ex.Message, existing?.Cursor, runKey, now, advanceClaim: false);
            await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Reviews, existingReviews,
                EvidenceCoverageStatus.PermissionLost, ex.Message, cursor: null, runKey, now, advanceClaim: false);
            return new StreamOutcome(0, EvidenceCoverageStatus.PermissionLost);
        }

        var rows = 0;
        foreach (var pullRequest in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RequireCollectionAsync(connection.TenantId);

            await run.TouchAsync();
            await repository.SaveRawAsync(
                connection.TenantId, connection.ConnectionKey, AzureDevOpsEvidenceMapper.ProviderName,
                connection.SourceAccountId, "pull_request",
                pullRequest.PullRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(pullRequest), now);

            foreach (var evidence in await mapper.MapPullRequestAsync(pullRequest, connection, repositoryKey, runKey, now))
            {
                await RequireCollectionAsync(connection.TenantId);
                await repository.UpsertEvidenceAsync(evidence);
                rows++;
            }
        }

        var status = page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial;

        await run.TouchAsync();
        await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Reviews, existingReviews,
            status, page.IsComplete ? null : "the pull request page was incomplete", cursor: null, runKey, now,
            advanceClaim: page.IsComplete);

        await run.TouchAsync();
        await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.PullRequests, existing,
            status, page.IncompleteReason, page.NextCursor, runKey, now, advanceClaim: page.IsComplete);

        return new StreamOutcome(rows, status);
    }

    /// <summary><paramref name="advanceClaim"/> is the honesty switch — see GitHubEvidenceIngestionService.</summary>
    private async Task RecordCoverageAsync(
        EvidenceConnection connection, string repositoryKey, EvidenceStream stream, EvidenceCoverage? existing,
        EvidenceCoverageStatus status, string? detail, string? cursor, Guid runKey, DateTime now, bool advanceClaim)
    {
        await RequireCollectionAsync(connection.TenantId);
        await repository.UpsertCoverageAsync(new EvidenceCoverage
        {
            CoverageKey = existing?.CoverageKey ?? Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            RepositoryKey = repositoryKey,
            Stream = stream,
            Cursor = cursor,
            ObservedFromUtc = existing?.ObservedFromUtc ?? now,
            CompleteThroughUtc = advanceClaim ? now : existing?.CompleteThroughUtc,
            Status = status,
            StatusDetail = detail,
            LastRunKey = runKey,
            LastAttemptedAtUtc = now,
            LastSucceededAtUtc = status == EvidenceCoverageStatus.Complete ? now : existing?.LastSucceededAtUtc,
            UpdatedAtUtc = now
        });
    }

    private async Task PurgeExpiredAsync(DateTime now)
    {
        if (options.Value.RawPayloadRetentionDays > 0)
        {
            await repository.PurgeRawBefore(now.AddDays(-options.Value.RawPayloadRetentionDays));
        }

        if (options.Value.SyncRunHistoryRetentionDays > 0)
        {
            await syncRunRepository.DeleteFinishedOlderThanAsync(now.AddDays(-options.Value.SyncRunHistoryRetentionDays));
        }
    }
}

/// <summary>
/// Azure DevOps' view of the "SkillsEvidence" configuration section. Bound
/// from the same section as the GitHub connector's options, so there is
/// one retention setting for evidence Bronze, not one per vendor — but a
/// separate type, so this vendor never references the GitHub namespace.
///
/// There is deliberately no token setting here. A personal access token is
/// a tenant's credential, entered by that tenant's admin; a deployment-wide
/// one would be exactly the fallback evidence sources forbid.
/// </summary>
public sealed class AzureDevOpsEvidenceOptions
{
    public int RawPayloadRetentionDays { get; set; } = 30;

    public int SyncRunHistoryRetentionDays { get; set; } = 180;
}
