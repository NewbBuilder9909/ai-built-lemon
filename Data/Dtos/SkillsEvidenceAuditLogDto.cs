using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Mirrors ContractAuditLogDto, with one deliberate difference: tenantId is
/// non-nullable here, because no row predates tenancy.
///
/// <see cref="DetailJson"/> never carries the free-text notes from an
/// assertion (the staff member's evidence note, the reviewer's rationale).
/// Those are the revealing part of the record and they go with the subject
/// on erasure; the audit log survives erasure by design, so anything put
/// here outlives it. What the log keeps is the accountability skeleton —
/// which skill, which status, which reviewer, when. See docs/gdpr.md and
/// SkillsEvidenceAuditDetailTests, which fails if a note reaches this
/// column.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SkillsEvidenceAuditLogDto
{
    public const string TableName = "SkillsEvidence_AuditLog";

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
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_AuditLog_entityId")]
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
