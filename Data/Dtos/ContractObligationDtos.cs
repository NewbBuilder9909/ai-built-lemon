using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>One engineering obligation from a contract clause. Withdrawn, never edited.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class ContractObligationDto
{
    public const string TableName = "ContractOps_Obligation";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("obligationKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ObligationKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("contractKey")]
    public Guid ContractKey { get; set; }

    [Column("kind")]
    [Length(32)]
    public string Kind { get; set; } = null!;

    [Column("clauseReference")]
    [Length(128)]
    public string ClauseReference { get; set; } = null!;

    [Column("description")]
    [Length(1000)]
    public string Description { get; set; } = null!;

    [Column("severityThreshold")]
    [Length(16)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SeverityThreshold { get; set; }

    [Column("gateAction")]
    [Length(32)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? GateAction { get; set; }

    [Column("remediationDays")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? RemediationDays { get; set; }

    [Column("recordedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? RecordedByStaffKey { get; set; }

    [Column("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; set; }

    [Column("withdrawnAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? WithdrawnAtUtc { get; set; }

    [Column("withdrawnByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? WithdrawnByStaffKey { get; set; }
}

/// <summary>A dated attestation of one repository control. Superseded, never edited.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RepositoryControlAttestationDto
{
    public const string TableName = "ContractOps_RepositoryControlAttestation";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("attestationKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid AttestationKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("repositoryKey")]
    [Length(256)]
    public string RepositoryKey { get; set; } = null!;

    [Column("control")]
    [Length(32)]
    public string Control { get; set; } = null!;

    [Column("state")]
    [Length(16)]
    public string State { get; set; } = null!;

    [Column("evidenceReference")]
    [Length(512)]
    public string EvidenceReference { get; set; } = null!;

    [Column("attestedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? AttestedByStaffKey { get; set; }

    [Column("attestedAtUtc")]
    public DateTime AttestedAtUtc { get; set; }

    [Column("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }

    [Column("supersededAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? SupersededAtUtc { get; set; }
}
