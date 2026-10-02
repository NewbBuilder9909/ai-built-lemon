using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Models.SecurityAssurance;

/// <summary>
/// Security findings and gate state, read from a scanning tool (Aikido first)
/// for step 3 of docs/delivery-evidence-and-contract-assurance.md.
/// Tool-neutral on purpose: nothing here names a vendor's types, so a second
/// scanner writes the same records (SourceIndependenceTests).
/// </summary>
public enum SecurityConnectionStatus
{
    Active = 0,
    Disconnected = 1,

    /// <summary>The stored credential no longer works; the admin must reconnect.</summary>
    AccessLost = 2,
}

/// <summary>A tenant's read-only connection to a scanning tool.</summary>
public sealed record SecurityToolConnection
{
    public required Guid ConnectionKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>The tool, e.g. "Aikido".</summary>
    public required string Tool { get; init; }

    /// <summary>The tool's region or instance, e.g. "eu".</summary>
    public required string Region { get; init; }

    public required string ClientId { get; init; }

    public string? ProtectedCredentialJson { get; init; }

    public required SecurityConnectionStatus Status { get; init; }

    public Guid? ConnectedByStaffKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public DateTime? LastSyncStartedAtUtc { get; init; }

    public DateTime? LastSyncSucceededAtUtc { get; init; }

    /// <summary>Why the last run did not complete, in words an admin can act on. Null after a clean run.</summary>
    public string? LastSyncError { get; init; }
}

/// <summary>Finding severity, ordered so a higher value is more severe.</summary>
public enum SecuritySeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

/// <summary>
/// The tool's status for a finding. Only <see cref="Closed"/> resolves one:
/// ignored and snoozed are the tool user's choice, not a contractual risk
/// acceptance, so they stay outstanding for assurance.
/// </summary>
public enum SecurityFindingStatus
{
    Open = 0,
    Snoozed = 1,
    Ignored = 2,
    Closed = 3,
}

/// <summary>
/// One finding in one repository: metadata only. File paths, line numbers,
/// snippets and titles are never stored, the same rule as engineering evidence.
/// </summary>
public sealed record SecurityFinding
{
    public required Guid TenantId { get; init; }

    public required string Tool { get; init; }

    /// <summary>The tool's id for the finding, stable across syncs.</summary>
    public required string ExternalId { get; init; }

    public required CodeRepositoryRef Repository { get; init; }

    public required SecuritySeverity Severity { get; init; }

    public required SecurityFindingStatus Status { get; init; }

    /// <summary>The tool's finding type, e.g. open_source, sast, leaked_secret.</summary>
    public required string FindingType { get; init; }

    public string? CveId { get; init; }

    public string? RuleId { get; init; }

    public string? AffectedPackage { get; init; }

    public required DateTime FirstDetectedAtUtc { get; init; }

    public DateTime? ClosedAtUtc { get; init; }

    public required DateTime LastSeenAtUtc { get; init; }

    public bool IsOutstanding => Status != SecurityFindingStatus.Closed;
}

/// <summary>
/// What the tool says about one repository right now: whether it scans it, and
/// how its pull-request gate is configured. An observation, not an attestation:
/// it supersedes a manual attestation for the same repository (decision 2).
/// </summary>
public sealed record RepositoryGateObservation
{
    public required Guid TenantId { get; init; }

    public required string Tool { get; init; }

    public required CodeRepositoryRef Repository { get; init; }

    /// <summary>The provider's own stable id (GitHub node id), so a rename is recognised.</summary>
    public string? ExternalRepoId { get; init; }

    /// <summary>The tool's id for the repository, which findings refer to.</summary>
    public required string ToolRepositoryId { get; init; }

    public DateTime? LastScannedAtUtc { get; init; }

    /// <summary>False when the tool has no pull-request check configuration for this repository.</summary>
    public required bool GateConfigured { get; init; }

    /// <summary>The lowest severity that fails the check; null when it never fails.</summary>
    public SecuritySeverity? GateMinimumSeverity { get; init; }

    public required bool FailsOnDependencies { get; init; }

    public required bool FailsOnCode { get; init; }

    public required bool FailsOnSecrets { get; init; }

    public required DateTime ObservedAtUtc { get; init; }
}

public enum CheckRunOutcome
{
    Passed = 0,
    Failed = 1,
    Bypassed = 2,
    TimedOut = 3,
    Pending = 4,
    Unknown = 5,
}

/// <summary>
/// One pull-request check run. Labelled unverified in the UI until a run has
/// been read from a live, gated repository (see the design doc).
/// </summary>
public sealed record SecurityCheckRun
{
    public required Guid TenantId { get; init; }

    public required string Tool { get; init; }

    public required string ExternalId { get; init; }

    public required CodeRepositoryRef Repository { get; init; }

    public required CheckRunOutcome Outcome { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public string? CommitSha { get; init; }

    public string? PullRequestUrl { get; init; }
}

/// <summary>Audit vocabulary for the security assurance area.</summary>
public static class SecurityAuditAction
{
    public const string EntityTypeConnection = "SecurityToolConnection";

    public const string ConnectionCreated = "ConnectionCreated";
    public const string ConnectionUpdated = "ConnectionUpdated";
    public const string ConnectionDisconnected = "ConnectionDisconnected";
    public const string SyncCompleted = "SyncCompleted";
}
