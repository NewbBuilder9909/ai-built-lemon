using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// One recorded Evidence Check. <c>tenantId</c> is non-nullable: this table
/// arrived after tenancy, so it has no legacy rows to accommodate
/// (docs/tenancy.md). Projects and source notes are stored as the check saw
/// them so the review's pack can be reproduced after Silver has moved on.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceReviewDto
{
    public const string TableName = "ProgrammeOps_EvidenceReview";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("reviewKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ReviewKey { get; set; }

    [Column("tenantId")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_EvidenceReview_tenantId")]
    public Guid TenantId { get; set; }

    [Column("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; set; }

    [Column("recordedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RecordedByStaffKey { get; set; }

    [Column("readiness")]
    [Length(32)]
    public string Readiness { get; set; } = null!;

    [Column("readinessReason")]
    [Length(1000)]
    public string ReadinessReason { get; set; } = null!;

    [Column("workItems")]
    public int WorkItems { get; set; }

    [Column("recordedHours")]
    public decimal RecordedHours { get; set; }

    [Column("projectsJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string ProjectsJson { get; set; } = null!;

    [Column("sourceNotesJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string SourceNotesJson { get; set; } = null!;

    // Scope (step 30). All null on reviews recorded before scopes existed,
    // which read the whole organisation over all time.

    /// <summary>EvidenceScope.Key: which earlier reviews this one is compared with.</summary>
    [Column("scopeKey")]
    [Length(80)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ScopeKey { get; set; }

    [Column("scopeProgrammeKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ScopeProgrammeKey { get; set; }

    [Column("scopeCustomerKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ScopeCustomerKey { get; set; }

    [Column("scopeLabel")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ScopeLabel { get; set; }

    [Column("periodFrom")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? PeriodFrom { get; set; }

    [Column("periodTo")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? PeriodTo { get; set; }

    [Column("extractedOn")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ExtractedOn { get; set; }

    [Column("decisionText")]
    [Length(500)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DecisionText { get; set; }
}

/// <summary>
/// One finding in a recorded check, with the decision taken on it. Unique on
/// <c>(tenantId, reviewKey, findingKey)</c> via a hand-written index (see
/// AddEvidenceReviewTables).
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceReviewFindingDto
{
    public const string TableName = "ProgrammeOps_EvidenceReviewFinding";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("reviewKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_EvidenceReviewFinding_reviewKey")]
    public Guid ReviewKey { get; set; }

    [Column("findingKey")]
    [Length(64)]
    public string FindingKey { get; set; } = null!;

    [Column("category")]
    [Length(32)]
    public string Category { get; set; } = null!;

    [Column("severity")]
    [Length(16)]
    public string Severity { get; set; } = null!;

    [Column("title")]
    [Length(256)]
    public string Title { get; set; } = null!;

    [Column("whyItMatters")]
    [Length(1000)]
    public string WhyItMatters { get; set; } = null!;

    [Column("numerator")]
    public decimal Numerator { get; set; }

    [Column("denominator")]
    public decimal Denominator { get; set; }

    [Column("unit")]
    [Length(64)]
    public string Unit { get; set; } = null!;

    [Column("recordsJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string RecordsJson { get; set; } = null!;

    [Column("disposition")]
    [Length(32)]
    public string Disposition { get; set; } = null!;

    [Column("ownerStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? OwnerStaffKey { get; set; }

    [Column("targetDate")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? TargetDate { get; set; }

    [Column("note")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Note { get; set; }

    [Column("decidedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DecidedAtUtc { get; set; }

    [Column("decidedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? DecidedByStaffKey { get; set; }

    [Column("carriedForward")]
    public bool CarriedForward { get; set; }
}
