using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.GitHub;

/// <summary>What one ingestion run did, in terms a reader can act on.</summary>
public sealed record EvidenceIngestionResult(
    int EvidenceRows,
    int RepositoriesCompleted,
    int RepositoriesPartial,
    int RepositoriesPermissionLost,
    int UnmappedActors,
    int BotActorsExcluded,
    string Summary);

/// <summary>
/// Runs one evidence ingestion for one connection.
///
/// The rule that shapes everything here: **an incomplete run must never
/// look like a complete one.** A failed page, a rate limit, a revoked
/// permission or a page-limit stop keeps whatever was fetched, leaves the
/// coverage claim where it was, and records the reason — so a portfolio
/// can say "we have not looked" rather than implying "there is nothing".
/// Deleting previously known facts on a partial run is explicitly ruled
/// out by the design document, and nothing here does it.
///
/// The cursor advances only after the page it covers has been stored,
/// which means a crash re-fetches rather than skips. Re-fetching is free:
/// the evidence upsert is idempotent on
/// (tenant, connection, sourceType, externalId, role).
/// </summary>
public sealed class GitHubEvidenceIngestionService(
    IGitHubEvidenceClient client,
    IEngineeringEvidenceRepository repository,
    IEvidenceCredentialProtector protector,
    IEvidenceActorResolver resolver,
    ISkillsEvidenceAuditLogRepository auditLog,
    IContinuityRepository continuity,
    ISyncRunRepository syncRunRepository,
    IOptions<SkillsEvidenceOptions> options,
    SyncRunCoordinator syncRunCoordinator,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The source name this connector holds its latch under. Distinct
    /// from every ProgrammeOps source, so an evidence sync and a ClickUp
    /// sync never serialize against each other.
    /// </summary>
    public const string SourceName = "GitHubEvidence";

    public async Task<EvidenceIngestionResult> RunAsync(
        Guid connectionKey, Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The Slice 4 gate, checked before anything is fetched. The
        // design document requires the customer's worker-notice,
        // lawful-basis and DPIA decision to be recorded *before*
        // person-level evidence is collected; a requirement that lives
        // only in a document is one the next person skips. The product
        // cannot judge whether their basis is sound — it can refuse to
        // collect until somebody has put their name to the question.
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

        // Evidence connections are shared across forges; this service
        // only knows how to read a GitHub one.
        if (!string.Equals(connection.Provider, GitHubEvidenceMapper.ProviderName, StringComparison.Ordinal))
        {
            throw new SkillAssertionValidationException("This is not a GitHub connection.");
        }

        if (connection.Status != EvidenceConnectionStatus.Active)
        {
            throw new SkillAssertionValidationException(
                "This connection is not active. Reconnect it before running an evidence sync.");
        }

        // No deployment-wide fallback. If this tenant's own credential
        // cannot be read, the run fails — it does not borrow one.
        var credential = protector.Unprotect(connection.ProtectedCredentialJson)
            ?? throw new SkillAssertionValidationException(
                "This connection has no readable credential. Reconnect it — there is no shared fallback for evidence sources.");

        if (client is GitHubEvidenceClient concrete)
        {
            concrete.AllowedEnterpriseHosts = options.Value.AllowedEnterpriseHosts;
        }

        await using var run = await syncRunCoordinator.TryBeginAsync(tenantId, SourceName, triggeredByMemberId)
            ?? throw new SkillAssertionValidationException(
                "An evidence sync is already running for this tenant (on this or another instance). Wait for it to finish.");

        var mapper = new GitHubEvidenceMapper(resolver);
        var rows = 0;
        var complete = 0;
        var partial = 0;
        var permissionLost = 0;

        try
        {
            await run.ReportStageAsync("checking selected repositories");

            // A repository dropped from the selection stops being synced, and
            // its coverage is withdrawn rather than its evidence deleted.
            await run.TouchAsync();
            await repository.MarkCoverageOutOfScopeAsync(connectionKey, connection.SelectedRepositories, tenantId, now);

            foreach (var repositoryKey in connection.SelectedRepositories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await run.ReportStageAsync($"syncing {repositoryKey}");

                var existing = await repository.GetCoverageForConnectionAsync(connectionKey, tenantId);

                foreach (var stream in new[] { EvidenceStream.Commits, EvidenceStream.PullRequests })
                {
                    var cursor = existing.FirstOrDefault(c =>
                        string.Equals(c.RepositoryKey, repositoryKey, StringComparison.OrdinalIgnoreCase) && c.Stream == stream);

                    var outcome = stream == EvidenceStream.Commits
                        ? await IngestCommitsAsync(connection, credential, repositoryKey, cursor, mapper, run.Run.RunKey, now, run, cancellationToken)
                        : await IngestPullRequestsAsync(connection, credential, repositoryKey, cursor, mapper, run.Run.RunKey, now, run, cancellationToken);

                    rows += outcome.Rows;

                    switch (outcome.Status)
                    {
                        case EvidenceCoverageStatus.Complete: complete++; break;
                        case EvidenceCoverageStatus.PermissionLost: permissionLost++; break;
                        default: partial++; break;
                    }
                }
            }

            // A connection that has lost access to every selected repository is
            // no longer usable, and saying so is more useful than letting every
            // later run fail the same way.
            if (permissionLost > 0 && complete == 0)
            {
                await run.ReportStageAsync("marking connection access lost");
                await run.TouchAsync();
                await repository.SetConnectionStatusAsync(
                    connectionKey, EvidenceConnectionStatus.AccessLost, tenantId, now, clearCredential: false);
            }

            var result = new EvidenceIngestionResult(
                rows, complete, partial, permissionLost, resolver.UnmappedCount, resolver.BotCount,
                $"{rows} evidence rows; {complete} streams complete, {partial} partial, {permissionLost} without permission; "
                + $"{resolver.UnmappedCount} unmapped {(resolver.UnmappedCount == 1 ? "account" : "accounts")}, "
                + $"{resolver.BotCount} bot {(resolver.BotCount == 1 ? "account" : "accounts")} excluded");

            await run.CompleteAsync(result.Summary, new
            {
                provider = GitHubEvidenceMapper.ProviderName,
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
                System.Text.Json.JsonSerializer.Serialize(new
                {
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
            await run.FailAsync(new OperationCanceledException("The evidence sync request was cancelled before it completed."));
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
                    provider = GitHubEvidenceMapper.ProviderName,
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
                $"The evidence sync stopped while {run.Stage ?? "running"} because another instance took over. Re-run it once the other run finishes.");
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
                    provider = GitHubEvidenceMapper.ProviderName,
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
                $"The evidence sync failed while {run.Stage ?? "running"}: {ex.Message} Re-run it once the cause is fixed — completed work is kept and re-running is safe.");
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
        EvidenceCoverage? existing, GitHubEvidenceMapper mapper, Guid runKey, DateTime now, SyncRunHandle run, CancellationToken cancellationToken)
    {
        GitHubPage<GitHubCommit> page;
        try
        {
            await RequireCollectionAsync(connection.TenantId);
            page = await client.GetDefaultBranchCommitsAsync(
                connection.ApiBaseUrl, credential.AccessToken, repositoryKey, existing?.Cursor, cancellationToken);
        }
        catch (GitHubAccessLostException ex)
        {
            await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Commits, existing,
                EvidenceCoverageStatus.PermissionLost, ex.Message, cursor: existing?.Cursor, runKey, now, advanceClaim: false);
            return new StreamOutcome(0, EvidenceCoverageStatus.PermissionLost);
        }

        var rows = 0;
        foreach (var commit in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RequireCollectionAsync(connection.TenantId);

            await run.TouchAsync();
            await repository.SaveRawAsync(
                connection.TenantId, connection.ConnectionKey, GitHubEvidenceMapper.ProviderName,
                connection.SourceAccountId, "commit", commit.Sha,
                System.Text.Json.JsonSerializer.Serialize(commit), now);

            foreach (var evidence in await mapper.MapCommitAsync(commit, connection, repositoryKey, runKey, now))
            {
                await RequireCollectionAsync(connection.TenantId);
                await repository.UpsertEvidenceAsync(evidence);
                rows++;
            }
        }

        await run.TouchAsync();
        await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Commits, existing,
            page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial,
            page.IncompleteReason, page.NextCursor, runKey, now, advanceClaim: page.IsComplete);

        return new StreamOutcome(rows, page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial);
    }

    private async Task<StreamOutcome> IngestPullRequestsAsync(
        EvidenceConnection connection, EvidenceCredential credential, string repositoryKey,
        EvidenceCoverage? existing, GitHubEvidenceMapper mapper, Guid runKey, DateTime now, SyncRunHandle run, CancellationToken cancellationToken)
    {
        GitHubPage<GitHubPullRequest> page;
        try
        {
            await RequireCollectionAsync(connection.TenantId);
            page = await client.GetMergedPullRequestsAsync(
                connection.ApiBaseUrl, credential.AccessToken, repositoryKey, existing?.Cursor, cancellationToken);
        }
        catch (GitHubAccessLostException ex)
        {
            await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.PullRequests, existing,
                EvidenceCoverageStatus.PermissionLost, ex.Message, cursor: existing?.Cursor, runKey, now, advanceClaim: false);
            return new StreamOutcome(0, EvidenceCoverageStatus.PermissionLost);
        }

        var rows = 0;
        foreach (var pullRequest in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RequireCollectionAsync(connection.TenantId);

            await run.TouchAsync();
            await repository.SaveRawAsync(
                connection.TenantId, connection.ConnectionKey, GitHubEvidenceMapper.ProviderName,
                connection.SourceAccountId, "pull_request", pullRequest.Id,
                System.Text.Json.JsonSerializer.Serialize(pullRequest), now);

            foreach (var evidence in await mapper.MapPullRequestAsync(pullRequest, connection, repositoryKey, runKey, now))
            {
                await RequireCollectionAsync(connection.TenantId);
                await repository.UpsertEvidenceAsync(evidence);
                rows++;
            }
        }

        // Reviews hang off the pull requests this run actually saw, so a
        // partial PR page means a partial review set too — recorded as its
        // own stream rather than folded into the PR one.
        var numbers = page.Items.Select(p => p.Number).ToList();
        if (numbers.Count > 0)
        {
            GitHubPage<GitHubReview>? reviews = null;
            try
            {
                await RequireCollectionAsync(connection.TenantId);
                reviews = await client.GetReviewsAsync(
                    connection.ApiBaseUrl, credential.AccessToken, repositoryKey, numbers, cancellationToken);
            }
            catch (GitHubAccessLostException ex)
            {
                await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Reviews, existing: null,
                    EvidenceCoverageStatus.PermissionLost, ex.Message, cursor: null, runKey, now, advanceClaim: false);
            }

            if (reviews is not null)
            {
                foreach (var review in reviews.Items)
                {
                    foreach (var evidence in await mapper.MapReviewAsync(review, connection, repositoryKey, runKey, now))
                    {
                        await RequireCollectionAsync(connection.TenantId);
                        await repository.UpsertEvidenceAsync(evidence);
                        rows++;
                    }
                }

                await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.Reviews, existing: null,
                    reviews.IsComplete && page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial,
                    reviews.IncompleteReason ?? (page.IsComplete ? null : "the pull request page was incomplete"),
                    cursor: null, runKey, now, advanceClaim: reviews.IsComplete && page.IsComplete);
            }
        }

        await run.TouchAsync();
        await RecordCoverageAsync(connection, repositoryKey, EvidenceStream.PullRequests, existing,
            page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial,
            page.IncompleteReason, page.NextCursor, runKey, now, advanceClaim: page.IsComplete);

        return new StreamOutcome(rows, page.IsComplete ? EvidenceCoverageStatus.Complete : EvidenceCoverageStatus.Partial);
    }

    /// <summary>
    /// <paramref name="advanceClaim"/> is the honesty switch.
    /// <c>CompleteThroughUtc</c> moves only when a stream finished cleanly;
    /// a partial run keeps the old claim while still storing what it
    /// fetched, so "no evidence" and "not looked" stay distinguishable.
    /// </summary>
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
        var days = options.Value.RawPayloadRetentionDays;
        if (days > 0)
        {
            await repository.PurgeRawBefore(now.AddDays(-days));
        }

        if (options.Value.SyncRunHistoryRetentionDays > 0)
        {
            await syncRunRepository.DeleteFinishedOlderThanAsync(now.AddDays(-options.Value.SyncRunHistoryRetentionDays));
        }
    }
}

/// <summary>
/// Deployment configuration for the evidence connectors, bound from
/// section "SkillsEvidence".
///
/// <see cref="AllowedEnterpriseHosts"/> is the only way a non-github.com
/// host can ever be reached. It is deliberately operator-supplied
/// configuration, not something a tenant admin can add through the UI —
/// that is the difference between a supported Enterprise deployment and
/// letting a form point the connector anywhere.
/// </summary>
public sealed class SkillsEvidenceOptions
{
    public const string SectionName = "SkillsEvidence";

    public string[] AllowedEnterpriseHosts { get; set; } = [];

    /// <summary>Bronze capture retention. 0 keeps them; mirrors ProgrammeOps:RawPayloadRetentionDays.</summary>
    public int RawPayloadRetentionDays { get; set; } = 30;

    /// <summary>Finished sync-run retention. 0 keeps them; mirrors ProgrammeOps:SyncRunHistoryRetentionDays.</summary>
    public int SyncRunHistoryRetentionDays { get; set; } = 180;

    public string? GitHubClientId { get; set; }

    public string? GitHubClientSecret { get; set; }

    public string? GitHubRedirectUri { get; set; }

    public string? GitHubAppSlug { get; set; }
}
