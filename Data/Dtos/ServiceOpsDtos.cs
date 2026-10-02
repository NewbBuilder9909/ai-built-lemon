using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>A tenant's desk connection. Unique on (tenantId, provider, sourceAccountId).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class DeskConnectionDto
{
    public const string TableName = "ServiceOps_DeskConnection";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("connectionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ConnectionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("displayName")]
    [Length(256)]
    public string DisplayName { get; set; } = null!;

    [Column("apiBaseUrl")]
    [Length(256)]
    public string ApiBaseUrl { get; set; } = null!;

    [Column("approvedComponentsJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string ApprovedComponentsJson { get; set; } = null!;

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("protectedCredentialJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ProtectedCredentialJson { get; set; }

    [Column("connectedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ConnectedByStaffKey { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }

    [Column("disconnectedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DisconnectedAtUtc { get; set; }
}

/// <summary>
/// Silver support case metadata, upserted idempotently on
/// (tenantId, connectionKey, externalTicketId).
///
/// There is no column for the ticket body, the requester, or any contact
/// detail, and there must never be one — the desk holds those, and this
/// product needs to count cases, not read them.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SupportCaseFactDto
{
    public const string TableName = "ServiceOps_SupportCaseFact";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("caseKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid CaseKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_SupportCaseFact_connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("externalTicketId")]
    [Length(128)]
    public string ExternalTicketId { get; set; } = null!;

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_SupportCaseFact_updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }

    [Column("resolvedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ResolvedAtUtc { get; set; }

    [Column("closedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ClosedAtUtc { get; set; }

    [Column("reopenedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReopenedAtUtc { get; set; }

    [Column("state")]
    [Length(32)]
    public string State { get; set; } = null!;

    [Column("providerStatus")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ProviderStatus { get; set; }

    [Column("priority")]
    [Length(32)]
    public string Priority { get; set; } = null!;

    [Column("componentKey")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ComponentKey { get; set; }

    [Column("rawComponentTag")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RawComponentTag { get; set; }

    [Column("caseType")]
    [Length(64)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? CaseType { get; set; }

    [Column("sourceUrl")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SourceUrl { get; set; }

    [Column("isReopened")]
    public bool IsReopened { get; set; }

    [Column("isWithdrawn")]
    public bool IsWithdrawn { get; set; }

    [Column("observedInRunKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ObservedInRunKey { get; set; }

    [Column("schemaVersion")]
    public int SchemaVersion { get; set; }

    [Column("firstIngestedAtUtc")]
    public DateTime FirstIngestedAtUtc { get; set; }

    [Column("ingestedAtUtc")]
    public DateTime IngestedAtUtc { get; set; }
}

/// <summary>Unique on (tenantId, connectionKey, externalTicketId, artifactType, artifactExternalId).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SupportCodeLinkDto
{
    public const string TableName = "ServiceOps_SupportCodeLink";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("linkKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LinkKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("externalTicketId")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_SupportCodeLink_ticket")]
    public string ExternalTicketId { get; set; } = null!;

    [Column("artifactType")]
    [Length(32)]
    public string ArtifactType { get; set; } = null!;

    [Column("artifactExternalId")]
    [Length(200)]
    public string ArtifactExternalId { get; set; } = null!;

    [Column("artifactSource")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ArtifactSource { get; set; }

    [Column("artifactUrl")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ArtifactUrl { get; set; }

    [Column("method")]
    [Length(32)]
    public string Method { get; set; } = null!;

    [Column("reviewedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ReviewedByStaffKey { get; set; }

    [Column("reviewedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReviewedAtUtc { get; set; }

    [Column("reviewNote")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ReviewNote { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Unique on (tenantId, connectionKey, externalTicketId, externalAgentId, role).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SupportCaseParticipantDto
{
    public const string TableName = "ServiceOps_CaseParticipant";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("participantKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ParticipantKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("externalTicketId")]
    [Length(128)]
    public string ExternalTicketId { get; set; } = null!;

    [Column("externalAgentId")]
    [Length(128)]
    public string ExternalAgentId { get; set; } = null!;

    [Column("agentDisplayName")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? AgentDisplayName { get; set; }

    [Column("role")]
    [Length(32)]
    public string Role { get; set; } = null!;

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_CaseParticipant_staffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? StaffKey { get; set; }

    [Column("occurredAtUtc")]
    public DateTime OccurredAtUtc { get; set; }

    [Column("ingestedAtUtc")]
    public DateTime IngestedAtUtc { get; set; }
}

/// <summary>Unique on (tenantId, connectionKey, externalAgentId).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class DeskAgentLinkDto
{
    public const string TableName = "ServiceOps_AgentLink";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("linkKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LinkKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("externalAgentId")]
    [Length(128)]
    public string ExternalAgentId { get; set; } = null!;

    [Column("externalAgentName")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalAgentName { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_AgentLink_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("approvedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ApprovedByStaffKey { get; set; }

    [Column("approvedAtUtc")]
    public DateTime ApprovedAtUtc { get; set; }
}

/// <summary>Unique on (tenantId, connectionKey, stream).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class DeskCoverageDto
{
    public const string TableName = "ServiceOps_Coverage";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("coverageKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid CoverageKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("stream")]
    [Length(32)]
    public string Stream { get; set; } = null!;

    [Column("cursor")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? Cursor { get; set; }

    [Column("observedFromUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ObservedFromUtc { get; set; }

    [Column("completeThroughUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? CompleteThroughUtc { get; set; }

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("statusDetail")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? StatusDetail { get; set; }

    [Column("lastRunKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? LastRunKey { get; set; }

    [Column("lastAttemptedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastAttemptedAtUtc { get; set; }

    [Column("lastSucceededAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastSucceededAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Bronze capture, purged on ServiceOps:RawPayloadRetentionDays after each successful run.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RawDeskPayloadDto
{
    public const string TableName = "ServiceOps_RawPayload";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("entityType")]
    [Length(32)]
    public string EntityType { get; set; } = null!;

    [Column("externalId")]
    [Length(128)]
    public string ExternalId { get; set; } = null!;

    [Column("payloadJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string PayloadJson { get; set; } = null!;

    [Column("fetchedAtUtc")]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_RawPayload_fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }
}

/// <summary>This area's own audit trail. tenantId non-nullable; nothing here predates tenancy.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class ServiceOpsAuditLogDto
{
    public const string TableName = "ServiceOps_AuditLog";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("logKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid LogKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("entityType")]
    [Length(64)]
    public string EntityType { get; set; } = null!;

    [Column("entityId")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_ServiceOps_AuditLog_entityId")]
    public string EntityId { get; set; } = null!;

    [Column("action")]
    [Length(64)]
    public string Action { get; set; } = null!;

    [Column("actorMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? ActorMemberId { get; set; }

    [Column("detailJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DetailJson { get; set; }

    [Column("timestampUtc")]
    public DateTime TimestampUtc { get; set; }
}
