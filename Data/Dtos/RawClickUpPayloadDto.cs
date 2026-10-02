using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Bronze layer: one row per raw ClickUp API response, captured verbatim
/// before anything is normalised. Nothing outside
/// Services/Integrations/ClickUp should read or write this table — Silver
/// and Gold code must never see ClickUp-shaped data.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RawClickUpPayloadDto
{
    public const string TableName = "ProgrammeOps_RawClickUpPayload";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("entityType")]
    [Length(32)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_RawClickUpPayload_entityType")]
    public string EntityType { get; set; } = null!;

    [Column("externalId")]
    [Length(128)]
    public string ExternalId { get; set; } = null!;

    [Column("workspaceId")]
    [Length(128)]
    public string WorkspaceId { get; set; } = null!;

    [Column("payloadJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string PayloadJson { get; set; } = null!;

    [Column("fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }

    [Column("processedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ProcessedAtUtc { get; set; }
}
