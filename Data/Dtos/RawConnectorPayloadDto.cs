using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>Tenant-owned Bronze capture for Jira and Tempo. Source account is recorded separately from provider name.</summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RawConnectorPayloadDto
{
    public const string TableName = "ProgrammeOps_RawConnectorPayload";
    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }
    [Column("tenantId")]
    public Guid TenantId { get; set; }
    [Column("source")]
    [Length(32)]
    public string Source { get; set; } = null!;
    [Column("sourceAccountId")]
    [Length(128)]
    public string SourceAccountId { get; set; } = null!;
    [Column("entityType")]
    [Length(32)]
    public string EntityType { get; set; } = null!;
    [Column("externalId")]
    [Length(128)]
    public string ExternalId { get; set; } = null!;
    [Column("payloadJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string PayloadJson { get; set; } = null!;
    [Column("fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }
}
