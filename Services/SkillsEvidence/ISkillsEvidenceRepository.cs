using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// One repository across both aggregates (definitions and assertions),
/// following the ProgrammeRepository precedent in CLAUDE.md rather than
/// splitting near-duplicate files per entity.
///
/// Every method takes <c>tenantId</c> and applies it inside the query, not
/// afterwards. Nothing here has a tenant-free overload: there is no caller
/// in this feature that legitimately reads across tenants, so the type
/// system should not offer one.
/// </summary>
public interface ISkillsEvidenceRepository
{
    // ---- Skill definitions ----

    /// <param name="includeRetired">Coverage and the declare form pass false; the taxonomy admin page passes true.</param>
    Task<IReadOnlyList<SkillDefinition>> GetSkillsAsync(Guid tenantId, bool includeRetired);

    Task<SkillDefinition?> GetSkillAsync(string skillKey, Guid tenantId);

    /// <summary>
    /// Inserts a definition. Throws <see cref="SkillAssertionValidationException"/>
    /// if the tenant already has this key — the unique index would throw
    /// anyway, but a driver-level constraint violation is not something a
    /// controller can turn into a sentence for the user.
    /// </summary>
    Task<SkillDefinition> CreateSkillAsync(SkillDefinition skill);

    /// <summary>Retire or reinstate. Definitions are never deleted — assertions point at them.</summary>
    Task<SkillDefinition> SetSkillActiveAsync(string skillKey, bool isActive, Guid tenantId, DateTime nowUtc);

    // ---- Assertions ----

    /// <summary>The one live assertion per skill for this person, newest first.</summary>
    Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForStaffAsync(Guid staffKey, Guid tenantId);

    /// <summary>Every row ever written for this person, oldest first — the correction/review history.</summary>
    Task<IReadOnlyList<StaffSkillAssertion>> GetHistoryForStaffAsync(Guid staffKey, Guid tenantId);

    /// <summary>Every live assertion in the tenant. Feeds the coverage aggregate and the review queue.</summary>
    Task<IReadOnlyList<StaffSkillAssertion>> GetCurrentForTenantAsync(Guid tenantId);

    Task<StaffSkillAssertion?> GetAssertionAsync(Guid assertionKey, Guid tenantId);

    /// <summary>
    /// The one write path for assertions, and the only place the append-only
    /// invariant lives. In a single transaction it stamps
    /// <c>supersededAtUtc</c> on the caller's <paramref name="supersedes"/>
    /// row (when there is one) and inserts <paramref name="assertion"/>, so
    /// the filtered unique index never sees two live rows for a
    /// (tenant, staff, skill).
    ///
    /// Verifies before writing that the skill definition and — when
    /// superseding — the previous row belong to <c>assertion.TenantId</c>,
    /// throwing <see cref="Shared.CrossTenantReferenceException"/>
    /// otherwise. Same guard as ProgrammeRepository and ContractRepository:
    /// a caller-supplied foreign key from another tenant is never silently
    /// accepted.
    /// </summary>
    Task<StaffSkillAssertion> AppendAsync(StaffSkillAssertion assertion, StaffSkillAssertion? supersedes, DateTime nowUtc);

    /// <summary>
    /// GDPR erasure. Deletes every assertion row for this person across all
    /// history and returns how many went, for the audit entry. Not
    /// tenant-scoped on purpose: erasure acts on the subject wherever their
    /// rows are, and the caller has already established which tenant owns
    /// them — see SkillsEvidenceDataParticipant.
    /// </summary>
    Task<int> DeleteAllForStaffAsync(Guid staffKey);
}
