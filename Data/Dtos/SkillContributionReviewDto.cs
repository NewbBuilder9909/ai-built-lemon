using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class SkillContributionReviewDto
{
    public const string TableName = "SkillsEvidence_ContributionReview";
    [Column("id"), PrimaryKeyColumn(AutoIncrement = true)] public int Id { get; set; }
    [Column("tenantId")] public Guid TenantId { get; set; }
    [Column("linkKey")] public Guid LinkKey { get; set; }
    [Column("revision")] public int Revision { get; set; }
    [Column("staffKey")] public Guid StaffKey { get; set; }
    [Column("assertionKey")] public Guid AssertionKey { get; set; }
    [Column("evidenceKey")] public Guid EvidenceKey { get; set; }
    [Column("status"), Length(32)] public string Status { get; set; } = null!;
    [Column("demonstrationNote"), Length(1000)] public string DemonstrationNote { get; set; } = null!;
    [Column("aiTool"), Length(100), NullSetting(NullSetting = NullSettings.Null)] public string? AiTool { get; set; }
    [Column("aiWorkflow"), Length(32)] public string AiWorkflow { get; set; } = null!;
    [Column("decisionNote"), Length(1000), NullSetting(NullSetting = NullSettings.Null)] public string? DecisionNote { get; set; }
    [Column("recordedByStaffKey")] public Guid RecordedByStaffKey { get; set; }
    [Column("recordedAtUtc")] public DateTime RecordedAtUtc { get; set; }
}
