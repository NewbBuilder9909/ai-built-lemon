using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// Bronze layer: one row per raw Hub Planner API response, captured verbatim
/// before anything is normalised. Same shape as RawClickUpPayloadDto (a
/// second, independent Bronze source for the same ProgrammeOps schema) —
/// nothing outside Services/Integrations/HubPlanner should read or write
/// this table.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class RawHubPlannerPayloadDto
{
    public const string TableName = "ProgrammeOps_RawHubPlannerPayload";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("entityType")]
    [Length(32)]
    [Index(IndexTypes.NonClustered, Name = "IX_ProgrammeOps_RawHubPlannerPayload_entityType")]
    public string EntityType { get; set; } = null!;

    [Column("externalId")]
    [Length(128)]
    public string ExternalId { get; set; } = null!;

    [Column("scopeId")]
    [Length(128)]
    public string ScopeId { get; set; } = null!;

    [Column("payloadJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string PayloadJson { get; set; } = null!;

    [Column("fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }

    [Column("processedAtUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? ProcessedAtUtc { get; set; }
}
