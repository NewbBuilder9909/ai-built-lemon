using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class SourceConnectionDto
{
    public const string TableName = "ProgrammeOps_SourceConnection";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("connectionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ConnectionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("source")]
    [Length(64)]
    public string Source { get; set; } = null!;

    [Column("displayName")]
    [Length(200)]
    public string DisplayName { get; set; } = null!;

    [Column("externalAccountId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ExternalAccountId { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Encrypted via ISourceCredentialProtector; never read or written except through it. Null means "use the deployment-wide credential".</summary>
    [Column("protectedCredentialJson")]
    [Length(2000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ProtectedCredentialJson { get; set; }
}
