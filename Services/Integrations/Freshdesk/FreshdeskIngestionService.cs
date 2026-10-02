using System.Text.Json;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.Freshdesk;

/// <summary>What one desk ingestion did, in terms a reader can act on.</summary>
public sealed record DeskIngestionResult(
    int CasesIngested,
    int Withdrawn,
    int Reopened,
    int UnmappedComponents,
    int LinksSuggested,
    bool IsComplete,
    string Summary);

/// <summary>
/// Runs one support-desk ingestion for one connection.
///
/// Two rules shape it, and both come from Freshdesk paginating by
/// <c>updated_since</c> watermark rather than by an opaque cursor:
///
/// **The window always overlaps.** The stored watermark is rewound by
/// <see cref="OverlapSeconds"/> before every run. Freshdesk's filter is
/// second-granular, so several tickets can share the boundary second and
/// resuming exactly at the watermark silently drops whichever of them the
/// previous page did not reach. Re-reading a few is free — the upsert is
/// idempotent on (tenant, connection, ticket) — whereas losing one is a
/// case that never appears in any figure.
///
/// **An incomplete run never looks complete.** A rate limit, a failed
/// page or exhausting the page budget keeps everything fetched, advances
/// the watermark so the next run continues, and records
/// <see cref="DeskCoverageStatus.Partial"/> with the reason. The coverage
/// *claim* only moves when the stream ends cleanly.
///
/// Withdrawn tickets are their own stream, because most desks exclude
/// deleted and spam from the main list. Without it a deleted ticket looks
/// like one that simply stopped being updated, and last month's demand
/// figure would quietly change.
/// </summary>
public sealed class FreshdeskIngestionService(
    IFreshdeskClient client,
    IServiceOpsRepository repository,
    IDeskCredentialProtector protector,
    ISupportCodeLinkService links,
    ISyncRunRepository syncRunRepository,
    IOptions<ServiceOpsOptions> options,
    SyncRunCoordinator syncRunCoordinator,
    TimeProvider timeProvider)
{
    /// <summary>The source name this connector holds its latch under.</summary>
    public const string SourceName = "FreshdeskSupport";

    /// <summary>
    /// How far the watermark is rewound each run. Generous on purpose:
    /// the cost is re-reading a handful of tickets, and the cost of
    /// getting it wrong is a case nobody ever sees.
    /// </summary>
    public const int OverlapSeconds = 120;

    public async Task<DeskIngestionResult> RunAsync(
        Guid connectionKey, Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var connection = await repository.GetConnectionAsync(connectionKey, tenantId)
            ?? throw new CrossTenantReferenceException("DeskConnection", connectionKey);

        if (connection.Status != DeskConnectionStatus.Active)
        {
            throw new ServiceOpsValidationException(
                "This desk connection is not active. Reconnect it before running a sync.");
        }

        // No deployment-wide fallback. An unreadable credential stops the
        // run rather than borrowing one from somewhere else.
        var credential = protector.Unprotect(connection.ProtectedCredentialJson)
            ?? throw new ServiceOpsValidationException(
                "This connection has no readable credential. Reconnect it — there is no shared fallback for desk sources.");

        var approved = connection.ApprovedComponents
            .Select(DeskHostPolicy.NormalizeComponent)
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var run = await syncRunCoordinator.TryBeginAsync(tenantId, SourceName, triggeredByMemberId)
            ?? throw new ServiceOpsValidationException(
                "A desk sync is already running for this tenant (on this or another instance). Wait for it to finish.");

        try
        {
            await run.ReportStageAsync("syncing active tickets");
            var tickets = await IngestStreamAsync(
                connection, credential, DeskStream.Tickets, approved, run.Run.RunKey, now, run, cancellationToken);

            await run.ReportStageAsync("syncing withdrawn tickets");
            var withdrawn = await IngestStreamAsync(
                connection, credential, DeskStream.WithdrawnTickets, approved, run.Run.RunKey, now, run, cancellationToken);

            var complete = tickets.Complete && withdrawn.Complete;

            if (!tickets.Complete && tickets.Cases == 0 && tickets.AccessLost)
            {
                await run.ReportStageAsync("marking desk access lost");
                await run.TouchAsync();
                await repository.SetConnectionStatusAsync(
                    connectionKey, DeskConnectionStatus.AccessLost, tenantId, now, clearCredential: false);
            }

            var result = new DeskIngestionResult(
                tickets.Cases, withdrawn.Cases, tickets.Reopened, tickets.UnmappedComponents,
                tickets.LinksSuggested, complete,
                $"{tickets.Cases} cases ({withdrawn.Cases} withdrawn), {tickets.Reopened} reopened, "
                + $"{tickets.UnmappedComponents} without an approved component, {tickets.LinksSuggested} relationship links suggested"
                + (complete ? string.Empty : " — run incomplete, the rest follow on the next sync"));

            await run.CompleteAsync(result.Summary, new
            {
                cases = tickets.Cases,
                withdrawn = withdrawn.Cases,
                tickets.Reopened,
                tickets.UnmappedComponents,
                tickets.LinksSuggested,
                complete,
                connectionKey
            });

            await repository.LogAsync(
                ServiceOpsAuditAction.EntityTypeConnection, connectionKey.ToString(),
                complete ? ServiceOpsAuditAction.SyncCompleted : ServiceOpsAuditAction.SyncFailed,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    runKey = run.Run.RunKey,
                    cases = tickets.Cases,
                    withdrawn = withdrawn.Cases,
                    tickets.Reopened,
                    tickets.UnmappedComponents,
                    tickets.LinksSuggested,
                    complete
                }),
                now, tenantId);

            await PurgeExpiredAsync(now);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await run.FailAsync(new OperationCanceledException("The desk sync request was cancelled before it completed."));
            throw;
        }
        catch (SyncLeaseLostException ex)
        {
            await run.FailAsync(ex);
            await repository.LogAsync(
                ServiceOpsAuditAction.EntityTypeConnection, connectionKey.ToString(),
                ServiceOpsAuditAction.SyncFailed,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    runKey = run.Run.RunKey,
                    reachedStage = run.Stage,
                    error = ex.Message
                }),
                timeProvider.GetUtcNow().UtcDateTime, tenantId);

            throw new ServiceOpsValidationException(
                $"The desk sync stopped while {run.Stage ?? "running"} because another instance took over. Re-run it once the other run finishes.");
        }
        catch (Exception ex)
        {
            await run.FailAsync(ex);
            await repository.LogAsync(
                ServiceOpsAuditAction.EntityTypeConnection, connectionKey.ToString(),
                ServiceOpsAuditAction.SyncFailed,
                triggeredByMemberId,
                JsonSerializer.Serialize(new
                {
                    runKey = run.Run.RunKey,
                    reachedStage = run.Stage,
                    error = ex.GetType().Name,
                    message = ex.Message
                }),
                timeProvider.GetUtcNow().UtcDateTime, tenantId);

            throw new ServiceOpsValidationException(
                $"The desk sync failed while {run.Stage ?? "running"}: {ex.Message} Re-run it once the cause is fixed — completed work is kept and re-running is safe.");
        }
    }

    private sealed record StreamOutcome(
        int Cases, int Reopened, int UnmappedComponents, int LinksSuggested, bool Complete, bool AccessLost);

    private async Task<StreamOutcome> IngestStreamAsync(
        DeskConnection connection, DeskCredential credential, DeskStream stream,
        HashSet<string> approvedComponents, Guid runKey, DateTime now, SyncRunHandle run, CancellationToken cancellationToken)
    {
        var existing = await repository.GetCoverageAsync(connection.ConnectionKey, stream, connection.TenantId);

        // The deliberate overlap. Safe only because the upsert is
        // idempotent; see the class remarks.
        var since = existing?.Cursor?.AddSeconds(-OverlapSeconds);

        FreshdeskPage page;
        try
        {
            page = stream == DeskStream.Tickets
                ? await client.GetTicketsUpdatedSinceAsync(connection.ApiBaseUrl, credential.ApiToken, since, cancellationToken)
                : await client.GetWithdrawnTicketsAsync(connection.ApiBaseUrl, credential.ApiToken, since, cancellationToken);
        }
        catch (DeskAccessLostException ex)
        {
            await RecordCoverageAsync(connection, stream, existing,
                DeskCoverageStatus.PermissionLost, ex.Message, existing?.Cursor, runKey, now, advanceClaim: false);
            return new StreamOutcome(0, 0, 0, 0, false, true);
        }

        var cases = 0;
        var reopened = 0;
        var unmapped = 0;
        var suggested = 0;

        foreach (var ticket in page.Tickets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await run.TouchAsync();
            await repository.SaveRawAsync(
                connection.TenantId, connection.ConnectionKey, FreshdeskCaseMapper.ProviderName,
                connection.SourceAccountId, "ticket", ticket.Id, JsonSerializer.Serialize(ticket), now);

            var fact = FreshdeskCaseMapper.Map(ticket, connection, approvedComponents, runKey, now);
            await repository.UpsertCaseAsync(fact);
            cases++;

            if (fact.IsReopened)
            {
                reopened++;
            }

            if (fact.HasUnmappedComponent && !fact.IsWithdrawn)
            {
                unmapped++;
            }

            // A withdrawn case keeps its participation and links — the
            // work happened — but contributes to no trend.
            if (FreshdeskCaseMapper.MapResolver(ticket, connection, now) is { } resolver)
            {
                await repository.UpsertParticipantAsync(resolver);
            }

            var suggestions = FreshdeskCaseMapper.MapSuggestedLinks(ticket, connection, now);
            if (suggestions.Count > 0)
            {
                suggested += await links.SuggestLinksAsync(suggestions, connection.TenantId, now);
            }
        }

        await run.TouchAsync();
        await RecordCoverageAsync(connection, stream, existing,
            page.IsComplete ? DeskCoverageStatus.Complete : DeskCoverageStatus.Partial,
            page.IncompleteReason, page.HighWatermarkUtc ?? existing?.Cursor, runKey, now,
            advanceClaim: page.IsComplete);

        return new StreamOutcome(cases, reopened, unmapped, suggested, page.IsComplete, false);
    }

    /// <summary>
    /// <paramref name="advanceClaim"/> is the honesty switch, exactly as
    /// in the evidence ingestion: the cursor may move so the next run
    /// resumes, but <c>CompleteThroughUtc</c> only moves when the stream
    /// actually finished. That is what keeps "no cases" and "we have not
    /// looked" distinguishable on the service view.
    /// </summary>
    private async Task RecordCoverageAsync(
        DeskConnection connection, DeskStream stream, DeskCoverage? existing,
        DeskCoverageStatus status, string? detail, DateTime? cursor, Guid runKey, DateTime now, bool advanceClaim)
    {
        await repository.UpsertCoverageAsync(new DeskCoverage
        {
            CoverageKey = existing?.CoverageKey ?? Guid.NewGuid(),
            TenantId = connection.TenantId,
            ConnectionKey = connection.ConnectionKey,
            Stream = stream,
            Cursor = cursor,
            ObservedFromUtc = existing?.ObservedFromUtc ?? now,
            CompleteThroughUtc = advanceClaim ? now : existing?.CompleteThroughUtc,
            Status = status,
            StatusDetail = detail,
            LastRunKey = runKey,
            LastAttemptedAtUtc = now,
            LastSucceededAtUtc = status == DeskCoverageStatus.Complete ? now : existing?.LastSucceededAtUtc,
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
/// Deployment configuration for the support-desk connectors, bound from
/// section "ServiceOps".
///
/// There is no host allow-list setting here, unlike the evidence
/// connectors: Freshdesk is per-customer subdomain, so the host rule is
/// structural (<see cref="DeskHostPolicy"/>) rather than a list an
/// operator maintains.
/// </summary>
public sealed class ServiceOpsOptions
{
    public const string SectionName = "ServiceOps";

    /// <summary>Bronze capture retention. 0 keeps them; mirrors the other areas' windows.</summary>
    public int RawPayloadRetentionDays { get; set; } = 30;

    /// <summary>Finished sync-run retention. 0 keeps them; mirrors ProgrammeOps:SyncRunHistoryRetentionDays.</summary>
    public int SyncRunHistoryRetentionDays { get; set; } = 180;

    /// <summary>
    /// Default reporting window for the service view, in days. A period
    /// is required for every comparison, so there is no "all time".
    /// </summary>
    public int DefaultReportingWindowDays { get; set; } = 90;
}
