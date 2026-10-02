using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// A proposal waiting for a human decision. Unique on
/// (tenantId, kind, subjectStaffKey, subjectKey, relatedKey) so
/// regenerating does not stack duplicates of the same proposal, and a
/// dismissal is not quietly re-raised on the next run.
///
/// <c>subjectStaffKey</c> is nullable and participates in the uniqueness,
/// so the index is filtered-free: SQL Server treats NULLs as equal for a
/// unique index, which is exactly the behaviour wanted here — two
/// component-level suggestions for the same component collapse to one.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class SuggestionDto
{
    public const string TableName = "SkillsEvidence_Suggestion";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("suggestionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid SuggestionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("kind")]
    [Length(32)]
    public string Kind { get; set; } = null!;

    [Column("subjectStaffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_Suggestion_subjectStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? SubjectStaffKey { get; set; }

    [Column("subjectKey")]
    [Length(200)]
    public string SubjectKey { get; set; } = null!;

    [Column("relatedKey")]
    [Length(200)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RelatedKey { get; set; }

    [Column("confidence")]
    [Length(32)]
    public string Confidence { get; set; } = null!;

    [Column("rationale")]
    [Length(1000)]
    public string Rationale { get; set; } = null!;

    /// <summary>JSON array of source URLs, bounded by SuggestionThresholds.MaxCitations.</summary>
    [Column("citationsJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? CitationsJson { get; set; }

    [Column("evidenceCount")]
    public int EvidenceCount { get; set; }

    [Column("observedFrom")]
    public DateTime ObservedFrom { get; set; }

    [Column("observedTo")]
    public DateTime ObservedTo { get; set; }

    [Column("outcome")]
    [Length(32)]
    public string Outcome { get; set; } = null!;

    [Column("decidedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? DecidedByStaffKey { get; set; }

    [Column("decidedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DecidedAtUtc { get; set; }

    [Column("decisionNote")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DecisionNote { get; set; }

    [Column("raisedAtUtc")]
    public DateTime RaisedAtUtc { get; set; }
}
