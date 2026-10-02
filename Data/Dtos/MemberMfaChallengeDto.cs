using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class MemberMfaChallengeDto
{
    public const string TableName = "StaffOps_MfaChallenge";
    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }
    [Column("challengeKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ChallengeKey { get; set; }
    [Column("memberId")]
    [Index(IndexTypes.NonClustered)]
    public int MemberId { get; set; }
    [Column("bindingHash")]
    [Length(64)]
    public string BindingHash { get; set; } = null!;
    [Column("securityStamp")]
    [Length(256)]
    public string SecurityStamp { get; set; } = null!;
    [Column("credentialVersion")]
    public Guid CredentialVersion { get; set; }
    [Column("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }
    [Column("failedAttempts")]
    public int FailedAttempts { get; set; }
}
