using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.SecurityAssurance;
using ProgrammePulse.Services.SecurityAssurance;

namespace ProgrammePulse.Tests.SecurityAssurance;

/// <summary>A snapshot a test sets directly; defaults to "no tool connected".</summary>
public sealed class FakeSecurityAssuranceQueryService : ISecurityAssuranceQueryService
{
    public SecurityAssuranceSnapshot Snapshot { get; set; } = SecurityAssuranceSnapshot.None;

    public Task<SecurityAssuranceSnapshot> GetSnapshotAsync(Guid tenantId, DateTime checkRunsSinceUtc) => Task.FromResult(Snapshot);

    public Task<SecurityConnectionSummary?> GetConnectionAsync(Guid tenantId, string tool) => Task.FromResult<SecurityConnectionSummary?>(null);
}

/// <summary>In-memory repository with the same replace and upsert semantics as the SQL one.</summary>
public sealed class FakeSecurityAssuranceRepository : ISecurityAssuranceRepository
{
    public List<SecurityToolConnection> Connections { get; } = [];
    public List<RepositoryGateObservation> Observations { get; } = [];
    public List<SecurityFinding> Findings { get; } = [];
    public List<SecurityCheckRun> CheckRuns { get; } = [];
    public List<(Guid TenantId, string Endpoint, int Page, string Json)> Raw { get; } = [];
    public List<(Guid TenantId, string Action, string? Detail)> Audit { get; } = [];
    public int PurgeCalls { get; private set; }

    public Task<SecurityToolConnection?> GetConnectionAsync(Guid tenantId, string tool) =>
        Task.FromResult(Connections.FirstOrDefault(c => c.TenantId == tenantId && c.Tool == tool));

    public Task UpsertConnectionAsync(SecurityToolConnection connection)
    {
        Connections.RemoveAll(c => c.TenantId == connection.TenantId && c.Tool == connection.Tool);
        Connections.Add(connection);
        return Task.CompletedTask;
    }

    public Task SetConnectionStatusAsync(Guid tenantId, string tool, SecurityConnectionStatus status, bool clearCredential, DateTime nowUtc) =>
        Update(tenantId, tool, c => c with
        {
            Status = status,
            ProtectedCredentialJson = clearCredential ? null : c.ProtectedCredentialJson,
            UpdatedAtUtc = nowUtc,
        });

    public Task RecordSyncStartedAsync(Guid tenantId, string tool, DateTime nowUtc) =>
        Update(tenantId, tool, c => c with { LastSyncStartedAtUtc = nowUtc });

    public Task RecordSyncFinishedAsync(Guid tenantId, string tool, string? error, DateTime nowUtc) =>
        Update(tenantId, tool, c => c with
        {
            LastSyncError = error,
            LastSyncSucceededAtUtc = error is null ? nowUtc : c.LastSyncSucceededAtUtc,
        });

    public Task ReplaceObservationsAsync(Guid tenantId, string tool, IReadOnlyList<RepositoryGateObservation> observations)
    {
        Observations.RemoveAll(o => o.TenantId == tenantId && o.Tool == tool);
        Observations.AddRange(observations);
        return Task.CompletedTask;
    }

    public Task UpsertFindingsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityFinding> findings)
    {
        foreach (var finding in findings)
        {
            Findings.RemoveAll(f => f.TenantId == tenantId && f.Tool == tool && f.ExternalId == finding.ExternalId);
            Findings.Add(finding);
        }

        return Task.CompletedTask;
    }

    public Task UpsertCheckRunsAsync(Guid tenantId, string tool, IReadOnlyList<SecurityCheckRun> runs)
    {
        foreach (var run in runs)
        {
            CheckRuns.RemoveAll(r => r.TenantId == tenantId && r.Tool == tool && r.ExternalId == run.ExternalId);
            CheckRuns.Add(run);
        }

        return Task.CompletedTask;
    }

    public Task SaveRawAsync(Guid tenantId, string tool, string endpoint, int page, string payloadJson, DateTime fetchedAtUtc)
    {
        Raw.Add((tenantId, endpoint, page, payloadJson));
        return Task.CompletedTask;
    }

    public Task<int> PurgeRawBeforeAsync(DateTime cutoffUtc)
    {
        PurgeCalls++;
        return Task.FromResult(0);
    }

    public Task<IReadOnlyList<RepositoryGateObservation>> GetObservationsAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<RepositoryGateObservation>>(Observations.Where(o => o.TenantId == tenantId).ToList());

    public Task<IReadOnlyList<SecurityFinding>> GetOutstandingFindingsAsync(Guid tenantId, SecuritySeverity minimumSeverity) =>
        Task.FromResult<IReadOnlyList<SecurityFinding>>(
            Findings.Where(f => f.TenantId == tenantId && f.IsOutstanding && f.Severity >= minimumSeverity).ToList());

    public Task<IReadOnlyList<SecurityCheckRun>> GetCheckRunsSinceAsync(Guid tenantId, DateTime sinceUtc) =>
        Task.FromResult<IReadOnlyList<SecurityCheckRun>>(CheckRuns.Where(r => r.TenantId == tenantId && r.StartedAtUtc >= sinceUtc).ToList());

    public Task LogAsync(Guid tenantId, string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc)
    {
        Audit.Add((tenantId, action, detailJson));
        return Task.CompletedTask;
    }

    private Task Update(Guid tenantId, string tool, Func<SecurityToolConnection, SecurityToolConnection> change)
    {
        var index = Connections.FindIndex(c => c.TenantId == tenantId && c.Tool == tool);
        if (index >= 0)
        {
            Connections[index] = change(Connections[index]);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Builders for security records, shared by the step 3 tests.</summary>
public static class SecurityRecords
{
    public static RepositoryGateObservation Observation(
        Guid tenantId, CodeRepositoryRef repository, bool configured = true, SecuritySeverity? threshold = SecuritySeverity.High,
        bool allKinds = true, DateTime? scanned = null, string toolRepositoryId = "1") => new()
    {
        TenantId = tenantId,
        Tool = SecurityTools.Aikido,
        Repository = repository,
        ToolRepositoryId = toolRepositoryId,
        LastScannedAtUtc = scanned ?? new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
        GateConfigured = configured,
        GateMinimumSeverity = configured ? threshold : null,
        FailsOnDependencies = configured && allKinds,
        FailsOnCode = configured && allKinds,
        FailsOnSecrets = configured,
        ObservedAtUtc = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc),
    };

    public static SecurityFinding Finding(
        Guid tenantId, CodeRepositoryRef repository, SecuritySeverity severity, SecurityFindingStatus status, DateTime firstDetected, string id = "10") => new()
    {
        TenantId = tenantId,
        Tool = SecurityTools.Aikido,
        ExternalId = id,
        Repository = repository,
        Severity = severity,
        Status = status,
        FindingType = "open_source",
        CveId = "CVE-2026-0001",
        AffectedPackage = "left-pad",
        FirstDetectedAtUtc = firstDetected,
        LastSeenAtUtc = firstDetected,
    };

    public static SecurityCheckRun Run(Guid tenantId, CodeRepositoryRef repository, CheckRunOutcome outcome, DateTime started, string id = "100") => new()
    {
        TenantId = tenantId,
        Tool = SecurityTools.Aikido,
        ExternalId = id,
        Repository = repository,
        Outcome = outcome,
        StartedAtUtc = started,
        CommitSha = "abcdef1234567",
    };

    public static SecurityAssuranceSnapshot Snapshot(
        IReadOnlyList<RepositoryGateObservation> observations,
        IReadOnlyList<SecurityFinding>? findings = null,
        IReadOnlyList<SecurityCheckRun>? runs = null) =>
        new(true, new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc), observations, findings ?? [], runs ?? []);
}
