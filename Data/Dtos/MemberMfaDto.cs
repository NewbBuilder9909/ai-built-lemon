using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class MemberMfaDto
{
    public const string TableName = "StaffOps_MemberMfa";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("memberId")]
    [Index(IndexTypes.UniqueNonClustered)]
    public int MemberId { get; set; }

    [Column("totpSecret")]
    [Length(64)]
    public string TotpSecret { get; set; } = null!;

    [Column("protectedTotpSecret")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ProtectedTotpSecret { get; set; }

    [Column("credentialVersion")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? CredentialVersion { get; set; }

    [Column("lastAcceptedTimeStep")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public long? LastAcceptedTimeStep { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("enabledAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? EnabledAtUtc { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>JSON array of {Hash, UsedAtUtc} — see MfaRecoveryCodeGenerator. Null until the member's first confirmed enrollment.</summary>
    [Column("recoveryCodesJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? RecoveryCodesJson { get; set; }
}
