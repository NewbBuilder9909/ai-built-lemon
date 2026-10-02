using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Append-only. A status change inserts a new row and stamps
/// <see cref="SupersededAtUtc"/> on the old one; nothing here is ever
/// updated in place except that one column. The "current" row is the one
/// where it is null, kept unique per (tenantId, staffKey, skillKey) by the
/// filtered index the migration creates.
///
/// Enums are persisted by name, not ordinal, so re-ordering
/// ProficiencyLevel later is a migration rather than a silent
/// reinterpretation of every stored row.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class StaffSkillAssertionDto
{
    public const string TableName = "SkillsEvidence_StaffSkillAssertion";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("assertionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid AssertionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_StaffSkillAssertion_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("skillKey")]
    [Length(128)]
    public string SkillKey { get; set; } = null!;

    [Column("proficiency")]
    [Length(32)]
    public string Proficiency { get; set; } = null!;

    [Column("origin")]
    [Length(32)]
    public string Origin { get; set; } = null!;

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    [Column("evidenceNote")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? EvidenceNote { get; set; }

    [Column("reviewerStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ReviewerStaffKey { get; set; }

    [Column("reviewedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReviewedAtUtc { get; set; }

    [Column("reviewNote")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ReviewNote { get; set; }

    [Column("reviewDueOn")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ReviewDueOn { get; set; }

    [Column("recordedByStaffKey")]
    public Guid RecordedByStaffKey { get; set; }

    [Column("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; set; }

    [Column("supersedesAssertionKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? SupersedesAssertionKey { get; set; }

    [Column("supersededAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? SupersededAtUtc { get; set; }
}
