namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// One skill a tenant has agreed to track. The tenant's own vocabulary —
/// there is no shared cross-tenant skill list, because "Billing" means a
/// different component to every customer and a global taxonomy would leak
/// one customer's product structure to another.
///
/// <see cref="TenantId"/> is non-nullable, unlike the tenant columns that
/// were retro-fitted onto Branding/Contract/Programme Ops. Nothing here
/// predates tenancy, so there is no legacy row to accommodate and no reason
/// to let an unscoped row exist — see docs/tenancy.md.
/// </summary>
public sealed record SkillDefinition
{
    public required Guid SkillDefinitionKey { get; init; }

    public required Guid TenantId { get; init; }

    /// <summary>Canonical identifier, unique within the tenant. See <see cref="SkillTaxonomy.NormalizeKey"/>.</summary>
    public required string SkillKey { get; init; }

    public required string Name { get; init; }

    public required SkillKind Kind { get; init; }

    /// <summary>
    /// The <see cref="SkillTaxonomy.CurrentVersion"/> in force when this row
    /// was written.
    /// </summary>
    public required int TaxonomyVersion { get; init; }

    /// <summary>
    /// Retired skills are kept, not deleted: existing assertions still point
    /// at them and their history has to stay readable. A retired skill
    /// cannot receive new assertions and is left out of coverage.
    /// </summary>
    public bool IsActive { get; init; } = true;

    public string? Description { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
