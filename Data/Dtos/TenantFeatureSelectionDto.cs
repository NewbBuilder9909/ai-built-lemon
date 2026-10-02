using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class TenantFeatureSelectionDto
{
    public const string TableName = "Tenancy_TenantFeatureSelection";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("tenantId")]
    [Index(IndexTypes.NonClustered)]
    public Guid TenantId { get; set; }

    [Column("featureKey")]
    [Length(64)]
    public string FeatureKey { get; set; } = null!;

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
