using NPoco;
using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.Shared;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.SecurityAssurance;

/// <summary>
/// Persistence for the security assurance area: the tool connection, findings,
/// repository gate observations, check runs, Bronze pages and the audit log.
/// One repository across them (ProgrammeRepository precedent). Every method
/// takes the tenant and applies it in SQL, except the retention purge, which
/// is platform-wide by age.
/// </summary>
public interface ISecurityAssuranceRepository
{
    Task<SecurityToolConnection?> GetConnectionAsync(Guid tenantId, string tool);

    Task UpsertConnectionAsync(SecurityToolConnection connection);

    Task SetConnectionStatusAsync(Guid tenantId, string tool, SecurityConnectionStatus status, bool clearCredential, DateTime nowUtc);

    Task RecordSyncStartedAsync(Guid tenantId, string tool, DateTime nowUtc);

    /// <summary>Records the end of a run: success clears the error; failure keeps the last success time.</summary>
    Task RecordSyncFinishedAsync(Guid tenantId, string tool, string? error, DateTime nowUtc);

    /// <summary>
    /// Replaces the tool's view of the tenant's repositories: upserts these, and
    /// removes observations of repositories the tool no longer lists, so a
    /// repository it stops scanning reads as unknown rather than keeping a stale gate.
    /// </summary>
    Task ReplaceObservationsAsync(Guid tenantId, string tool, IReadOnlyList<RepositoryGateObservation> observations);

    Task UpsertFindingsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityFinding> findings);

    Task UpsertCheckRunsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityCheckRun> runs);

    Task SaveRawAsync(Guid tenantId, string tool, string endpoint, int page, string payloadJson, DateTime fetchedAtUtc);

    /// <summary>Retention purge of Bronze pages older than the cutoff, across tenants; returns rows deleted.</summary>
    Task<int> PurgeRawBeforeAsync(DateTime cutoffUtc);

    Task<IReadOnlyList<RepositoryGateObservation>> GetObservationsAsync(Guid tenantId);

    /// <summary>Findings not closed, at or above a severity. Ignored and snoozed count as outstanding.</summary>
    Task<IReadOnlyList<SecurityFinding>> GetOutstandingFindingsAsync(Guid tenantId, SecuritySeverity minimumSeverity);

    Task<IReadOnlyList<SecurityCheckRun>> GetCheckRunsSinceAsync(Guid tenantId, DateTime sinceUtc);

    Task LogAsync(Guid tenantId, string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc);
}

public sealed class SecurityAssuranceRepository(IScopeProvider scopeProvider) : ISecurityAssuranceRepository
{
    public async Task<SecurityToolConnection?> GetConnectionAsync(Guid tenantId, string tool)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<SecurityConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND tool = @1", tenantId, tool));
        return dto is null ? null : Map(dto);
    }

    public async Task UpsertConnectionAsync(SecurityToolConnection connection)
    {
        using var scope = scopeProvider.CreateScope();
        var existing = await scope.Database.FirstOrDefaultAsync<SecurityConnectionDto>(
            Sql.Builder.Where("tenantId = @0 AND tool = @1", connection.TenantId, connection.Tool));

        var dto = existing ?? new SecurityConnectionDto
        {
            ConnectionKey = connection.ConnectionKey,
            TenantId = connection.TenantId,
            Tool = connection.Tool,
            CreatedAtUtc = connection.CreatedAtUtc,
        };
        dto.Region = connection.Region;
        dto.ClientId = connection.ClientId;
        dto.ProtectedCredentialJson = connection.ProtectedCredentialJson;
        dto.Status = connection.Status.ToString();
        dto.ConnectedByStaffKey = connection.ConnectedByStaffKey;
        dto.UpdatedAtUtc = connection.UpdatedAtUtc;
        dto.LastSyncError = null;

        if (existing is null)
        {
            await scope.Database.InsertAsync(dto);
        }
        else
        {
            await scope.Database.UpdateAsync(dto);
        }

        scope.Complete();
    }

    public async Task SetConnectionStatusAsync(Guid tenantId, string tool, SecurityConnectionStatus status, bool clearCredential, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {SecurityConnectionDto.TableName} SET status = @0, updatedAtUtc = @1" +
            (clearCredential ? ", protectedCredentialJson = NULL" : string.Empty) +
            " WHERE tenantId = @2 AND tool = @3",
            new object[] { status.ToString(), nowUtc, tenantId, tool });
        scope.Complete();
    }

    public async Task RecordSyncStartedAsync(Guid tenantId, string tool, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {SecurityConnectionDto.TableName} SET lastSyncStartedAtUtc = @0 WHERE tenantId = @1 AND tool = @2",
            new object[] { nowUtc, tenantId, tool });
        scope.Complete();
    }

    public async Task RecordSyncFinishedAsync(Guid tenantId, string tool, string? error, DateTime nowUtc)
    {
        using var scope = scopeProvider.CreateScope();
        if (error is null)
        {
            await scope.Database.ExecuteAsync(
                $"UPDATE {SecurityConnectionDto.TableName} SET lastSyncSucceededAtUtc = @0, lastSyncError = NULL WHERE tenantId = @1 AND tool = @2",
                new object[] { nowUtc, tenantId, tool });
        }
        else
        {
            await scope.Database.ExecuteAsync(
                $"UPDATE {SecurityConnectionDto.TableName} SET lastSyncError = @0 WHERE tenantId = @1 AND tool = @2",
                new object[] { error.Length > 512 ? error[..512] : error, tenantId, tool });
        }

        scope.Complete();
    }

    public async Task ReplaceObservationsAsync(Guid tenantId, string tool, IReadOnlyList<RepositoryGateObservation> observations)
    {
        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;
        var existing = (await database.FetchAsync<RepositoryGateObservationDto>(
                Sql.Builder.Where("tenantId = @0 AND tool = @1", tenantId, tool)))
            .ToDictionary(d => d.ToolRepositoryId, StringComparer.Ordinal);

        var inserts = new List<object?[]>();
        var updates = new List<object?[]>();
        foreach (var o in observations)
        {
            object?[] values =
            [
                o.Repository.Provider, o.Repository.SourceAccountId, o.Repository.RepositoryKey, o.ExternalRepoId, o.LastScannedAtUtc,
                o.GateConfigured, o.GateMinimumSeverity?.ToString(), o.FailsOnDependencies, o.FailsOnCode, o.FailsOnSecrets, o.ObservedAtUtc
            ];
            if (existing.Remove(o.ToolRepositoryId, out var row))
            {
                updates.Add([row.Id, .. values]);
            }
            else
            {
                inserts.Add([tenantId, tool, o.ToolRepositoryId, .. values]);
            }
        }

        string[] valueColumns =
        [
            "provider", "sourceAccountId", "repositoryKey", "externalRepoId", "lastScannedAtUtc",
            "gateConfigured", "gateMinimumSeverity", "failsOnDependencies", "failsOnCode", "failsOnSecrets", "observedAtUtc"
        ];
        await SqlMultiRow.UpdateAsync(database, RepositoryGateObservationDto.TableName, "id", valueColumns, updates, tenantId);
        await SqlMultiRow.InsertAsync(database, RepositoryGateObservationDto.TableName, ["tenantId", "tool", "toolRepositoryId", .. valueColumns], inserts);

        // Anything left was not listed this time: the tool no longer scans it.
        foreach (var chunk in existing.Values.Select(d => d.Id).Chunk(SqlInList.ChunkSize))
        {
            await database.ExecuteAsync(
                $"DELETE FROM {RepositoryGateObservationDto.TableName} WHERE tenantId = @0 AND tool = @1 AND id IN (@2)", tenantId, tool, chunk);
        }

        scope.Complete();
    }

    public async Task UpsertFindingsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityFinding> findings)
    {
        string[] valueColumns =
        [
            "provider", "sourceAccountId", "repositoryKey", "severity", "status", "findingType", "cveId", "ruleId",
            "affectedPackage", "firstDetectedAtUtc", "closedAtUtc", "lastSeenAtUtc"
        ];
        await UpsertByExternalIdAsync(SecurityFindingDto.TableName, tenantId, tool, valueColumns,
            findings.Select(f => (f.ExternalId, (object?[])
            [
                f.Repository.Provider, f.Repository.SourceAccountId, f.Repository.RepositoryKey, f.Severity.ToString(), f.Status.ToString(),
                f.FindingType, f.CveId, f.RuleId, f.AffectedPackage, f.FirstDetectedAtUtc, f.ClosedAtUtc, f.LastSeenAtUtc
            ])).ToList());
    }

    public async Task UpsertCheckRunsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityCheckRun> runs)
    {
        string[] valueColumns = ["provider", "sourceAccountId", "repositoryKey", "outcome", "startedAtUtc", "commitSha", "pullRequestUrl"];
        await UpsertByExternalIdAsync(SecurityCheckRunDto.TableName, tenantId, tool, valueColumns,
            runs.Select(r => (r.ExternalId, (object?[])
            [
                r.Repository.Provider, r.Repository.SourceAccountId, r.Repository.RepositoryKey, r.Outcome.ToString(),
                r.StartedAtUtc, r.CommitSha, r.PullRequestUrl
            ])).ToList());
    }

    // Set-based upsert keyed on (tenant, tool, externalId): existing rows are
    // updated by id, new ones inserted, all in one transaction.
    private async Task UpsertByExternalIdAsync(string table, Guid tenantId, string tool, string[] valueColumns, IReadOnlyList<(string ExternalId, object?[] Values)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using var scope = scopeProvider.CreateScope();
        var database = scope.Database;
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chunk in rows.Select(r => r.ExternalId).Distinct(StringComparer.Ordinal).Chunk(SqlInList.ChunkSize))
        {
            foreach (var row in await database.FetchAsync<IdRow>(
                         $"SELECT id AS Id, externalId AS ExternalId FROM {SqlIdentifier.Quote(table)} WHERE tenantId = @0 AND tool = @1 AND externalId IN (@2)", tenantId, tool, chunk))
            {
                ids[row.ExternalId] = row.Id;
            }
        }

        // Last occurrence wins if the tool repeats an id within one export.
        var latest = rows.GroupBy(r => r.ExternalId, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        await SqlMultiRow.UpdateAsync(database, table, "id", valueColumns,
            latest.Where(r => ids.ContainsKey(r.ExternalId)).Select(r => (object?[])[ids[r.ExternalId], .. r.Values]), tenantId);
        await SqlMultiRow.InsertAsync(database, table, ["tenantId", "tool", "externalId", .. valueColumns],
            latest.Where(r => !ids.ContainsKey(r.ExternalId)).Select(r => (object?[])[tenantId, tool, r.ExternalId, .. r.Values]));
        scope.Complete();
    }

    private sealed class IdRow
    {
        public int Id { get; set; }

        public string ExternalId { get; set; } = string.Empty;
    }

    public async Task SaveRawAsync(Guid tenantId, string tool, string endpoint, int page, string payloadJson, DateTime fetchedAtUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new SecurityRawPayloadDto
        {
            TenantId = tenantId, Tool = tool, Endpoint = endpoint, Page = page, PayloadJson = payloadJson, FetchedAtUtc = fetchedAtUtc
        });
        scope.Complete();
    }

    public async Task<int> PurgeRawBeforeAsync(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        var deleted = await scope.Database.ExecuteAsync($"DELETE FROM {SecurityRawPayloadDto.TableName} WHERE fetchedAtUtc < @0", cutoffUtc);
        scope.Complete();
        return deleted;
    }

    public async Task<IReadOnlyList<RepositoryGateObservation>> GetObservationsAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<RepositoryGateObservationDto>(Sql.Builder.Where("tenantId = @0", tenantId));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<SecurityFinding>> GetOutstandingFindingsAsync(Guid tenantId, SecuritySeverity minimumSeverity)
    {
        var severities = Enum.GetValues<SecuritySeverity>().Where(s => s >= minimumSeverity).Select(s => s.ToString()).ToArray();
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SecurityFindingDto>(
            Sql.Builder.Where("tenantId = @0 AND status <> @1 AND severity IN (@2)", tenantId, SecurityFindingStatus.Closed.ToString(), severities));
        return dtos.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<SecurityCheckRun>> GetCheckRunsSinceAsync(Guid tenantId, DateTime sinceUtc)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<SecurityCheckRunDto>(
            Sql.Builder.Where("tenantId = @0 AND startedAtUtc >= @1", tenantId, sinceUtc).OrderBy("startedAtUtc DESC", "id DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task LogAsync(Guid tenantId, string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc)
    {
        using var scope = scopeProvider.CreateScope();
        await scope.Database.InsertAsync(new SecurityAuditLogDto
        {
            TenantId = tenantId, EntityType = entityType, EntityId = entityId, Action = action,
            ActorMemberId = actorMemberId, DetailJson = detailJson, TimestampUtc = timestampUtc
        });
        scope.Complete();
    }

    private static SecurityToolConnection Map(SecurityConnectionDto d) => new()
    {
        ConnectionKey = d.ConnectionKey, TenantId = d.TenantId, Tool = d.Tool, Region = d.Region, ClientId = d.ClientId,
        ProtectedCredentialJson = d.ProtectedCredentialJson, Status = Enum.Parse<SecurityConnectionStatus>(d.Status),
        ConnectedByStaffKey = d.ConnectedByStaffKey, CreatedAtUtc = d.CreatedAtUtc, UpdatedAtUtc = d.UpdatedAtUtc,
        LastSyncStartedAtUtc = d.LastSyncStartedAtUtc, LastSyncSucceededAtUtc = d.LastSyncSucceededAtUtc, LastSyncError = d.LastSyncError,
    };

    private static RepositoryGateObservation Map(RepositoryGateObservationDto d) => new()
    {
        TenantId = d.TenantId, Tool = d.Tool, Repository = new CodeRepositoryRef(d.Provider, d.SourceAccountId, d.RepositoryKey),
        ExternalRepoId = d.ExternalRepoId, ToolRepositoryId = d.ToolRepositoryId, LastScannedAtUtc = d.LastScannedAtUtc,
        GateConfigured = d.GateConfigured,
        GateMinimumSeverity = d.GateMinimumSeverity is null ? null : Enum.Parse<SecuritySeverity>(d.GateMinimumSeverity),
        FailsOnDependencies = d.FailsOnDependencies, FailsOnCode = d.FailsOnCode, FailsOnSecrets = d.FailsOnSecrets, ObservedAtUtc = d.ObservedAtUtc,
    };

    private static SecurityFinding Map(SecurityFindingDto d) => new()
    {
        TenantId = d.TenantId, Tool = d.Tool, ExternalId = d.ExternalId,
        Repository = new CodeRepositoryRef(d.Provider, d.SourceAccountId, d.RepositoryKey),
        Severity = Enum.Parse<SecuritySeverity>(d.Severity), Status = Enum.Parse<SecurityFindingStatus>(d.Status), FindingType = d.FindingType,
        CveId = d.CveId, RuleId = d.RuleId, AffectedPackage = d.AffectedPackage,
        FirstDetectedAtUtc = d.FirstDetectedAtUtc, ClosedAtUtc = d.ClosedAtUtc, LastSeenAtUtc = d.LastSeenAtUtc,
    };

    private static SecurityCheckRun Map(SecurityCheckRunDto d) => new()
    {
        TenantId = d.TenantId, Tool = d.Tool, ExternalId = d.ExternalId,
        Repository = new CodeRepositoryRef(d.Provider, d.SourceAccountId, d.RepositoryKey),
        Outcome = Enum.Parse<CheckRunOutcome>(d.Outcome), StartedAtUtc = d.StartedAtUtc, CommitSha = d.CommitSha, PullRequestUrl = d.PullRequestUrl,
    };
}
