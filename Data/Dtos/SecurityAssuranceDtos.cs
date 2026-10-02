using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>A tenant's read-only connection to a scanning tool; one per tenant and tool.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SecurityConnectionDto
{
    public const string TableName = "SecurityAssurance_Connection";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("connectionKey")] [Index(IndexTypes.UniqueNonClustered)] public Guid ConnectionKey { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("tool")] [Length(32)] public string Tool { get; set; } = null!;
    [Column("region")] [Length(16)] public string Region { get; set; } = null!;
    [Column("clientId")] [Length(256)] public string ClientId { get; set; } = null!;
    [Column("protectedCredentialJson")] [SpecialDbType(SpecialDbTypes.NVARCHARMAX)] [NullSetting(NullSetting = NullSettings.Null)] public string? ProtectedCredentialJson { get; set; }
    [Column("status")] [Length(16)] public string Status { get; set; } = null!;
    [Column("connectedByStaffKey")] [NullSetting(NullSetting = NullSettings.Null)] public Guid? ConnectedByStaffKey { get; set; }
    [Column("createdAtUtc")] public DateTime CreatedAtUtc { get; set; }
    [Column("updatedAtUtc")] public DateTime UpdatedAtUtc { get; set; }
    [Column("lastSyncStartedAtUtc")] [NullSetting(NullSetting = NullSettings.Null)] public DateTime? LastSyncStartedAtUtc { get; set; }
    [Column("lastSyncSucceededAtUtc")] [NullSetting(NullSetting = NullSettings.Null)] public DateTime? LastSyncSucceededAtUtc { get; set; }
    [Column("lastSyncError")] [Length(512)] [NullSetting(NullSetting = NullSettings.Null)] public string? LastSyncError { get; set; }
}

/// <summary>One finding's metadata. Upserted by (tenant, tool, externalId), so a replayed export is idempotent.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SecurityFindingDto
{
    public const string TableName = "SecurityAssurance_Finding";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("tool")] [Length(32)] public string Tool { get; set; } = null!;
    [Column("externalId")] [Length(64)] public string ExternalId { get; set; } = null!;
    [Column("provider")] [Length(32)] public string Provider { get; set; } = null!;
    [Column("sourceAccountId")] [Length(128)] public string SourceAccountId { get; set; } = null!;
    [Column("repositoryKey")] [Length(256)] public string RepositoryKey { get; set; } = null!;
    [Column("severity")] [Length(16)] public string Severity { get; set; } = null!;
    [Column("status")] [Length(16)] public string Status { get; set; } = null!;
    [Column("findingType")] [Length(32)] public string FindingType { get; set; } = null!;
    [Column("cveId")] [Length(64)] [NullSetting(NullSetting = NullSettings.Null)] public string? CveId { get; set; }
    [Column("ruleId")] [Length(128)] [NullSetting(NullSetting = NullSettings.Null)] public string? RuleId { get; set; }
    [Column("affectedPackage")] [Length(256)] [NullSetting(NullSetting = NullSettings.Null)] public string? AffectedPackage { get; set; }
    [Column("firstDetectedAtUtc")] public DateTime FirstDetectedAtUtc { get; set; }
    [Column("closedAtUtc")] [NullSetting(NullSetting = NullSettings.Null)] public DateTime? ClosedAtUtc { get; set; }
    [Column("lastSeenAtUtc")] public DateTime LastSeenAtUtc { get; set; }
}

/// <summary>The latest observation of one repository in one tool. Upserted by (tenant, tool, toolRepositoryId).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RepositoryGateObservationDto
{
    public const string TableName = "SecurityAssurance_RepositoryObservation";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("tool")] [Length(32)] public string Tool { get; set; } = null!;
    [Column("toolRepositoryId")] [Length(64)] public string ToolRepositoryId { get; set; } = null!;
    [Column("provider")] [Length(32)] public string Provider { get; set; } = null!;
    [Column("sourceAccountId")] [Length(128)] public string SourceAccountId { get; set; } = null!;
    [Column("repositoryKey")] [Length(256)] public string RepositoryKey { get; set; } = null!;
    [Column("externalRepoId")] [Length(128)] [NullSetting(NullSetting = NullSettings.Null)] public string? ExternalRepoId { get; set; }
    [Column("lastScannedAtUtc")] [NullSetting(NullSetting = NullSettings.Null)] public DateTime? LastScannedAtUtc { get; set; }
    [Column("gateConfigured")] public bool GateConfigured { get; set; }
    [Column("gateMinimumSeverity")] [Length(16)] [NullSetting(NullSetting = NullSettings.Null)] public string? GateMinimumSeverity { get; set; }
    [Column("failsOnDependencies")] public bool FailsOnDependencies { get; set; }
    [Column("failsOnCode")] public bool FailsOnCode { get; set; }
    [Column("failsOnSecrets")] public bool FailsOnSecrets { get; set; }
    [Column("observedAtUtc")] public DateTime ObservedAtUtc { get; set; }
}

/// <summary>One pull-request check run. Upserted by (tenant, tool, externalId).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SecurityCheckRunDto
{
    public const string TableName = "SecurityAssurance_CheckRun";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("tool")] [Length(32)] public string Tool { get; set; } = null!;
    [Column("externalId")] [Length(64)] public string ExternalId { get; set; } = null!;
    [Column("provider")] [Length(32)] public string Provider { get; set; } = null!;
    [Column("sourceAccountId")] [Length(128)] public string SourceAccountId { get; set; } = null!;
    [Column("repositoryKey")] [Length(256)] public string RepositoryKey { get; set; } = null!;
    [Column("outcome")] [Length(16)] public string Outcome { get; set; } = null!;
    [Column("startedAtUtc")] public DateTime StartedAtUtc { get; set; }
    [Column("commitSha")] [Length(64)] [NullSetting(NullSetting = NullSettings.Null)] public string? CommitSha { get; set; }
    [Column("pullRequestUrl")] [Length(512)] [NullSetting(NullSetting = NullSettings.Null)] public string? PullRequestUrl { get; set; }
}

/// <summary>Bronze: one API response page, verbatim, purged after the retention window.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SecurityRawPayloadDto
{
    public const string TableName = "SecurityAssurance_RawPayload";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("tool")] [Length(32)] public string Tool { get; set; } = null!;
    [Column("endpoint")] [Length(128)] public string Endpoint { get; set; } = null!;
    [Column("page")] public int Page { get; set; }
    [Column("payloadJson")] [SpecialDbType(SpecialDbTypes.NVARCHARMAX)] public string PayloadJson { get; set; } = null!;
    [Column("fetchedAtUtc")] public DateTime FetchedAtUtc { get; set; }
}

/// <summary>Who connected, disconnected or synced what, per tenant.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SecurityAuditLogDto
{
    public const string TableName = "SecurityAssurance_AuditLog";

    [Column("id")] [PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("entityType")] [Length(64)] public string EntityType { get; set; } = null!;
    [Column("entityId")] [Length(64)] public string EntityId { get; set; } = null!;
    [Column("action")] [Length(64)] public string Action { get; set; } = null!;
    [Column("actorMemberId")] [NullSetting(NullSetting = NullSettings.Null)] public int? ActorMemberId { get; set; }
    [Column("detailJson")] [SpecialDbType(SpecialDbTypes.NVARCHARMAX)] [NullSetting(NullSetting = NullSettings.Null)] public string? DetailJson { get; set; }
    [Column("timestampUtc")] public DateTime TimestampUtc { get; set; }
}
