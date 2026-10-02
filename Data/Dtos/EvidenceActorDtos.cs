using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// An approved external-account-to-staff mapping. Unique on
/// (tenantId, connectionKey, externalActorId) via a named index: one
/// account maps to at most one person, per connection.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceActorLinkDto
{
    public const string TableName = "SkillsEvidence_ActorLink";

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

    [Column("externalActorId")]
    [Length(128)]
    public string ExternalActorId { get; set; } = null!;

    [Column("externalLogin")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalLogin { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_ActorLink_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("approvedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ApprovedByStaffKey { get; set; }

    [Column("approvedAtUtc")]
    public DateTime ApprovedAtUtc { get; set; }
}

/// <summary>
/// The visible queue of external accounts nobody has identified. Unique on
/// (tenantId, connectionKey, externalActorId) so repeated sightings update
/// one row rather than growing the queue.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class UnmappedEvidenceActorDto
{
    public const string TableName = "SkillsEvidence_UnmappedActor";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("unmappedActorKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid UnmappedActorKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("externalActorId")]
    [Length(128)]
    public string ExternalActorId { get; set; } = null!;

    [Column("externalLogin")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalLogin { get; set; }

    [Column("displayName")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DisplayName { get; set; }

    [Column("email")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Email { get; set; }

    [Column("isBot")]
    public bool IsBot { get; set; }

    [Column("reason")]
    [Length(32)]
    public string Reason { get; set; } = null!;

    [Column("suggestedStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? SuggestedStaffKey { get; set; }

    [Column("occurrenceCount")]
    public int OccurrenceCount { get; set; }

    [Column("firstSeenUtc")]
    public DateTime FirstSeenUtc { get; set; }

    [Column("lastSeenUtc")]
    public DateTime LastSeenUtc { get; set; }

    [Column("resolvedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ResolvedAtUtc { get; set; }
}

/// <summary>
/// Incremental position and coverage honesty per
/// (tenantId, connectionKey, repositoryKey, stream) — unique on that
/// composite.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceCoverageDto
{
    public const string TableName = "SkillsEvidence_Coverage";

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

    [Column("repositoryKey")]
    [Length(256)]
    public string RepositoryKey { get; set; } = null!;

    [Column("stream")]
    [Length(32)]
    public string Stream { get; set; } = null!;

    [Column("cursor")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Cursor { get; set; }

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

/// <summary>
/// Bronze capture for evidence sources. Retained on a short window
/// (SkillsEvidence:RawPayloadRetentionDays) and purged after every
/// successful run, mirroring the ProgrammeOps Bronze tables.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RawEvidencePayloadDto
{
    public const string TableName = "SkillsEvidence_RawPayload";

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
    [Length(200)]
    public string ExternalId { get; set; } = null!;

    [Column("payloadJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string PayloadJson { get; set; } = null!;

    [Column("fetchedAtUtc")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_RawPayload_fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }
}
