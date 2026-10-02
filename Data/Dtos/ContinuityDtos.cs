using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>Manager-approved component ownership. Unique on (tenantId, componentKey).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class ComponentOwnershipDto
{
    public const string TableName = "SkillsEvidence_ComponentOwnership";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("ownershipKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid OwnershipKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("componentKey")]
    [Length(128)]
    public string ComponentKey { get; set; } = null!;

    [Column("displayName")]
    [Length(200)]
    public string DisplayName { get; set; } = null!;

    [Column("description")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Description { get; set; }

    [Column("ownerStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? OwnerStaffKey { get; set; }

    [Column("reviewedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ReviewedByStaffKey { get; set; }

    [Column("lastReviewedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? LastReviewedAtUtc { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Approved cover. Unique on (tenantId, componentKey, staffKey).</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class ComponentBackupDto
{
    public const string TableName = "SkillsEvidence_ComponentBackup";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("backupKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid BackupKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("componentKey")]
    [Length(128)]
    public string ComponentKey { get; set; } = null!;

    [Column("staffKey")]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_ComponentBackup_staffKey")]
    public Guid StaffKey { get; set; }

    [Column("approvedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ApprovedByStaffKey { get; set; }

    [Column("approvedAtUtc")]
    public DateTime ApprovedAtUtc { get; set; }

    [Column("note")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Note { get; set; }
}

/// <summary>The Platinum layer: reviewed decisions with owners and outcomes.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class CoverageActionDto
{
    public const string TableName = "SkillsEvidence_CoverageAction";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("actionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ActionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("componentKey")]
    [Length(128)]
    [Index(IndexTypes.NonClustered, Name = "IX_SkillsEvidence_CoverageAction_componentKey")]
    public string ComponentKey { get; set; } = null!;

    [Column("actionType")]
    [Length(32)]
    public string ActionType { get; set; } = null!;

    /// <summary>Null when the owner has been erased — see CoverageAction.OwnerStaffKey.</summary>
    [Column("ownerStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? OwnerStaffKey { get; set; }

    [Column("rationale")]
    [Length(1000)]
    public string Rationale { get; set; } = null!;

    [Column("evidenceSnapshot")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? EvidenceSnapshot { get; set; }

    [Column("dueOn")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DueOn { get; set; }

    [Column("outcome")]
    [Length(32)]
    public string Outcome { get; set; } = null!;

    [Column("outcomeNote")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? OutcomeNote { get; set; }

    [Column("raisedByStaffKey")]
    public Guid RaisedByStaffKey { get; set; }

    [Column("raisedAtUtc")]
    public DateTime RaisedAtUtc { get; set; }

    [Column("closedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ClosedByStaffKey { get; set; }

    [Column("closedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>
/// The tenant's recorded lawful basis, worker notice and DPIA decision
/// for person-level evidence. Append-only in effect: a superseded
/// decision is withdrawn rather than edited, so the history of what was
/// relied on and when stays readable. A filtered unique index keeps at
/// most one live decision per tenant.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceProcessingDecisionDto
{
    public const string TableName = "SkillsEvidence_ProcessingDecision";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("decisionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid DecisionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("lawfulBasis")]
    [Length(32)]
    public string LawfulBasis { get; set; } = null!;

    [Column("workerNoticeGiven")]
    public bool WorkerNoticeGiven { get; set; }

    [Column("workerNoticeReference")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? WorkerNoticeReference { get; set; }

    [Column("dpiaCompleted")]
    public bool DpiaCompleted { get; set; }

    [Column("dpiaReference")]
    [Length(512)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? DpiaReference { get; set; }

    [Column("dpiaCompletedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DpiaCompletedAtUtc { get; set; }

    [Column("purpose")]
    [Length(1000)]
    public string Purpose { get; set; } = null!;

    [Column("decidedByStaffKey")]
    public Guid DecidedByStaffKey { get; set; }

    [Column("decidedAtUtc")]
    public DateTime DecidedAtUtc { get; set; }

    [Column("reviewDueOn")]
    public DateTime ReviewDueOn { get; set; }

    [Column("withdrawnAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? WithdrawnAtUtc { get; set; }
}
