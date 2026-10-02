using System.Text.Json;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.SecurityAssurance;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Integrations.Aikido;

/// <summary>
/// Runs one Aikido read for one tenant: repositories and their PR-check
/// configuration, every issue's metadata, and recent PR-check runs.
///
/// **A partial read never replaces observations.** Observations are replaced
/// wholesale so a repository Aikido stops scanning drops to "unknown"; doing
/// that from a truncated repository list would wrongly drop repositories that
/// are still scanned. So an incomplete run upserts what it read, records why it
/// was incomplete, and leaves the last clean sync time where it was.
/// </summary>
public sealed class AikidoIngestionService(
    IAikidoClient client,
    ISecurityAssuranceRepository repository,
    ISecurityCredentialProtector protector,
    SyncRunGuard runGuard,
    TimeProvider timeProvider)
{
    public const string SourceName = "AikidoSecurity";

    /// <summary>Bronze pages are kept this long, then purged after each clean run.</summary>
    public const int RawPayloadRetentionDays = 30;

    public async Task<CommandResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default)
    {
        using var latch = runGuard.TryEnter(tenantId, SourceName);
        if (latch is null)
        {
            return CommandResult.Refused("An Aikido sync is already running. Wait for it to finish.");
        }

        var connection = await repository.GetConnectionAsync(tenantId, SecurityTools.Aikido);
        if (connection is null || connection.Status == SecurityConnectionStatus.Disconnected)
        {
            return CommandResult.Refused("Connect Aikido before running a sync.");
        }

        var started = timeProvider.GetUtcNow().UtcDateTime;
        var credential = protector.Unprotect(connection.ProtectedCredentialJson);
        if (credential is null)
        {
            await repository.SetConnectionStatusAsync(tenantId, SecurityTools.Aikido, SecurityConnectionStatus.AccessLost, clearCredential: false, started);
            await repository.RecordSyncFinishedAsync(tenantId, SecurityTools.Aikido, "The stored credential can no longer be read. Reconnect Aikido.", started);
            return CommandResult.Refused("The stored Aikido credential can no longer be read. Reconnect Aikido.");
        }

        await repository.RecordSyncStartedAsync(tenantId, SecurityTools.Aikido, started);

        AikidoReadResult read;
        try
        {
            read = await client.ReadAsync(connection.Region, credential.ClientId, credential.ClientSecret, cancellationToken);
        }
        catch (AikidoAccessLostException ex)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await repository.SetConnectionStatusAsync(tenantId, SecurityTools.Aikido, SecurityConnectionStatus.AccessLost, clearCredential: false, now);
            await repository.RecordSyncFinishedAsync(tenantId, SecurityTools.Aikido, ex.Message, now);
            return CommandResult.Refused(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            const string message = "Aikido could not be reached. Nothing was changed; try again later.";
            await repository.RecordSyncFinishedAsync(tenantId, SecurityTools.Aikido, message, timeProvider.GetUtcNow().UtcDateTime);
            return CommandResult.Refused(message);
        }

        var fetched = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var page in read.Repositories.Concat(read.CheckConfigurations).Concat(read.Issues).Concat(read.CheckRuns))
        {
            await repository.SaveRawAsync(tenantId, SecurityTools.Aikido, page.Endpoint, page.Page, page.RedactedJson, fetched);
        }

        var observations = AikidoMapper.Observations(
            tenantId, read.Repositories.SelectMany(p => p.Items), read.CheckConfigurations.SelectMany(p => p.Items), fetched);
        var findings = AikidoMapper.Findings(tenantId, read.Issues.SelectMany(p => p.Items), observations, fetched);
        var runs = AikidoMapper.CheckRuns(tenantId, read.CheckRuns.SelectMany(p => p.Items), observations);

        var repositoriesComplete = read.IsComplete;
        if (repositoriesComplete)
        {
            await repository.ReplaceObservationsAsync(tenantId, SecurityTools.Aikido, observations);
        }

        await repository.UpsertFindingsAsync(tenantId, SecurityTools.Aikido, findings);
        await repository.UpsertCheckRunsAsync(tenantId, SecurityTools.Aikido, runs);

        var finished = timeProvider.GetUtcNow().UtcDateTime;
        await repository.RecordSyncFinishedAsync(tenantId, SecurityTools.Aikido, read.IncompleteReason, finished);
        if (read.IsComplete && connection.Status == SecurityConnectionStatus.AccessLost)
        {
            await repository.SetConnectionStatusAsync(tenantId, SecurityTools.Aikido, SecurityConnectionStatus.Active, clearCredential: false, finished);
        }

        if (read.IsComplete)
        {
            await repository.PurgeRawBeforeAsync(finished.AddDays(-RawPayloadRetentionDays));
        }

        var gated = observations.Count(o => o.GateConfigured);
        var outstanding = findings.Count(f => f.IsOutstanding && f.Severity >= SecuritySeverity.High);
        await repository.LogAsync(
            tenantId, SecurityAuditAction.EntityTypeConnection, connection.ConnectionKey.ToString(), SecurityAuditAction.SyncCompleted,
            triggeredByMemberId,
            JsonSerializer.Serialize(new
            {
                complete = read.IsComplete,
                repositories = observations.Count,
                gated,
                findings = findings.Count,
                outstandingHighOrCritical = outstanding,
                checkRuns = runs.Count,
            }),
            finished);

        var summary = $"Read {observations.Count} repositories ({gated} with a PR-check gate), {findings.Count} findings "
            + $"({outstanding} High or Critical not closed) and {runs.Count} PR-check runs.";
        return read.IsComplete
            ? CommandResult.Succeeded(summary)
            : CommandResult.Refused($"{summary} The read was incomplete: {read.IncompleteReason}.");
    }
}
