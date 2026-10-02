using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

[TableName(TableName)]
[ExplicitColumns]
internal sealed class SkillDefinitionDto
{
    public const string TableName = "SkillsEvidence_SkillDefinition";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("skillDefinitionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid SkillDefinitionKey { get; set; }

    /// <summary>
    /// Not nullable, unlike the tenant columns retro-fitted onto the older
    /// feature areas — nothing here predates tenancy, so there is no legacy
    /// row to accommodate. Uniqueness of (tenantId, skillKey) is a named
    /// index created by the migration, not an attribute, because the
    /// annotation set here has no composite form.
    /// </summary>
    [Column("tenantId")]
    public Guid TenantId { get; set; }

    [Column("skillKey")]
    [Length(128)]
    public string SkillKey { get; set; } = null!;

    [Column("name")]
    [Length(200)]
    public string Name { get; set; } = null!;

    [Column("kind")]
    [Length(32)]
    public string Kind { get; set; } = null!;

    [Column("taxonomyVersion")]
    public int TaxonomyVersion { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }

    [Column("description")]
    [Length(1000)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Description { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [Column("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}
