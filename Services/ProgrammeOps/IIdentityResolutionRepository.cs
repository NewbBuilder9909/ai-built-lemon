using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Persistence for the two identity-resolution aggregates — explicit links
/// (ExternalIdentityLink) and the unresolved queue (UnresolvedIdentity).
/// One repository for both, following the ProgrammeRepository precedent:
/// they are always read together by StaffIdentityResolver and the identity
/// queue page. Every method except the two age-based retention purges takes
/// an explicit tenantId and filters/stamps by it — deleting a stale or
/// GDPR-erased row regardless of tenant isn't a cross-tenant leak, so those
/// two stay global.
/// </summary>
public interface IIdentityResolutionRepository
{
    Task<IReadOnlyList<ExternalIdentityLink>> GetLinksAsync(Guid tenantId);

    Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForSourceAsync(string externalSource, Guid tenantId);

    /// <summary>
    /// Unscoped by tenant, like DeleteLinksForStaffAsync — safe because
    /// staffKey already is the isolation boundary here: GDPR export/erasure
    /// callers (StaffAdminController) prove the key belongs to the caller's
    /// tenant before reaching this, and no link can exist for a staffKey
    /// outside its owner's tenant in the first place.
    /// </summary>
    Task<IReadOnlyList<ExternalIdentityLink>> GetLinksForStaffAsync(Guid staffKey);

    Task<ExternalIdentityLink> CreateLinkAsync(ExternalIdentityLink link, Guid tenantId);

    Task DeleteLinkAsync(Guid linkKey, Guid tenantId);

    /// <summary>Only rows not yet resolved, most frequently seen first.</summary>
    Task<IReadOnlyList<UnresolvedIdentity>> GetUnresolvedAsync(Guid tenantId);

    /// <summary>One page of the open queue, in the same order as <see cref="GetUnresolvedAsync"/>.</summary>
    async Task<ResultPage<UnresolvedIdentity>> GetUnresolvedPageAsync(Guid tenantId, PageRequest page) =>
        ResultPage<UnresolvedIdentity>.Of(await GetUnresolvedAsync(tenantId), page);

    /// <summary>How many identities are waiting, without reading them.</summary>
    async Task<int> CountUnresolvedAsync(Guid tenantId) => (await GetUnresolvedAsync(tenantId)).Count;

    Task<UnresolvedIdentity?> GetUnresolvedByKeyAsync(Guid unresolvedIdentityKey, Guid tenantId);

    /// <summary>
    /// Upsert keyed on (tenant, source, external user id, email): a new
    /// sighting increments OccurrenceCount and moves LastSeenUtc; a first
    /// sighting inserts. A previously resolved row seen again is re-opened
    /// (its ResolvedAtUtc cleared) because the link that resolved it
    /// evidently no longer matches — that must be visible, not silently
    /// absorbed. <paramref name="suggestedStaffKey"/> is the latest sighting's
    /// email suggestion, replacing any earlier one (null clears it).
    /// </summary>
    Task<UnresolvedIdentity> RecordSightingAsync(string externalSource, string? externalUserId, string? email, string? displayName, string context, DateTime nowUtc, Guid tenantId, Guid? suggestedStaffKey = null);

    Task MarkResolvedAsync(Guid unresolvedIdentityKey, Guid staffKey, DateTime nowUtc, Guid tenantId);

    /// <summary>Retention purge of unresolved rows not seen since the cutoff; returns rows deleted.</summary>
    Task<int> DeleteUnresolvedNotSeenSinceAsync(DateTime cutoffUtc);

    /// <summary>GDPR erasure support: every link pointing at this staff member.</summary>
    Task DeleteLinksForStaffAsync(Guid staffKey);
}
