using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// A tenant's read-only connection to one source account. Unique on
/// (tenantId, provider, sourceAccountId) via a named index, so one tenant
/// may hold several accounts of the same provider.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class EvidenceConnectionDto
{
    public const string TableName = "SkillsEvidence_Connection";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("connectionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid ConnectionKey { get; set; }

    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("provider")]
    [Length(32)]
    public string Provider { get; set; } = null!;

    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;

    [Column("displayName")]
    [Length(256)]
    public string DisplayName { get; set; } = null!;

    [Column("installationId")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? InstallationId { get; set; }

    [Column("apiBaseUrl")]
    [Length(256)]
    public string ApiBaseUrl { get; set; } = null!;

    /// <summary>JSON array of "owner/name". Bounded to 200 entries by EvidenceHostPolicy before it reaches here.</summary>
    [Column("selectedRepositoriesJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string SelectedRepositoriesJson { get; set; } = null!;

    [Column("status")]
    [Length(32)]
    public string Status { get; set; } = null!;

    /// <summary>Opaque ciphertext. Never logged, never rendered, never in a URL.</summary>
    [Column("protectedCredentialJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ProtectedCredentialJson { get; set; }

    [Column("connectedByStaffKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ConnectedByStaffKey { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }

    [Column("disconnectedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? DisconnectedAtUtc { get; set; }
}
