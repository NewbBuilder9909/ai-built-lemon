using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Silver engineering evidence. Upserted idempotently on
/// (tenantId, connectionKey, sourceType, externalId, role) — a named unique
/// index, because that composite is what makes a replay safe: re-fetching
/// a page updates rows instead of duplicating them.
///
/// Carries metadata and links only. No diff, no patch, no file contents.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EngineeringEvidenceDto
{
    public const string TableName = "SkillsEvidence_EngineeringEvidence";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("evidenceKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid EvidenceKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("connectionKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_EngineeringEvidence_connectionKey")]
    public Guid ConnectionKey { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("sourceType")]
    [Length(32)]
    public string SourceType { get; set; } = null!;

    [Column("externalId")]
    [Length(200)]
    public string ExternalId { get; set; } = null!;

    [Column("role")]
    [Length(32)]
    public string Role { get; set; } = null!;

    [Column("actorExternalId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ActorExternalId { get; set; }

    [Column("actorLogin")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ActorLogin { get; set; }

    [Column("actorEmail")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ActorEmail { get; set; }

    [Column("actorIsBot")]
    public bool ActorIsBot { get; set; }

    /// <summary>Null unless an approved EvidenceActorLink exists. Never set by a heuristic.</summary>
    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_EngineeringEvidence_staffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? StaffKey { get; set; }

    [Column("attributionStatus")]
    [Length(32)]
    public string AttributionStatus { get; set; } = null!;

    [Column("repositoryKey")]
    [Length(256)]
    public string RepositoryKey { get; set; } = null!;

    [Column("title")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Title { get; set; }

    [Column("sourceUrl")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SourceUrl { get; set; }

    [Column("occurredAtUtc")]
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>JSON array of language keys. Unvalidated evidence; see ChangedPathClassifier.</summary>
    [Column("languageHintsJson")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? LanguageHintsJson { get; set; }

    /// <summary>See EngineeringEvidence.AuthorshipVerified. Null on rows written before schema version 2.</summary>
    [Column("authorshipVerified")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public bool? AuthorshipVerified { get; set; }

    [Column("observedInRunKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ObservedInRunKey { get; set; }

    [Column("schemaVersion")]
    public int SchemaVersion { get; set; }

    [Column("firstIngestedAtUtc")]
    public DateTime FirstIngestedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
