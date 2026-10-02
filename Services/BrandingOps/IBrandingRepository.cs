using ProgrammePulse.Models.Branding;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>
/// One repository across the branding aggregate (profile lifecycle + asset
/// metadata), following the ProgrammeRepository precedent of covering
/// several related entities rather than one file per entity. Every method
/// takes a mandatory tenantId — Branding Ops is the pilot for real tenant
/// data isolation (see docs/tenancy.md); the filter lives in each method's
/// own query, not applied to results afterward.
/// </summary>
public interface IBrandingRepository
{
    Task<BrandingProfile?> GetActivePublishedAsync(Guid tenantId);

    Task<BrandingProfile?> GetDraftAsync(Guid tenantId);

    Task<IReadOnlyList<BrandingProfile>> GetHistoryAsync(Guid tenantId);

    Task<BrandingProfile> SaveDraftAsync(BrandingProfile draft, Guid tenantId);

    /// <summary>
    /// Archives the current Published row (if any) and promotes the Draft row
    /// to Published, setting EffectiveFromUtc to now.
    /// </summary>
    Task<BrandingProfile> PublishAsync(int? actorMemberId, Guid tenantId);

    /// <summary>
    /// Archives the current Published row (if any) and inserts a new
    /// Published row cloned from the given archived version's token values.
    /// targetVersionKey is looked up scoped to tenantId — without that
    /// clause this would let one tenant roll back to another tenant's
    /// archived version, since VersionKey alone is only globally unique, not
    /// tenant-scoped.
    /// </summary>
    Task<BrandingProfile> RollbackAsync(Guid targetVersionKey, int? actorMemberId, Guid tenantId);

    Task<BrandingAsset> SaveAssetAsync(BrandingAsset asset, Guid tenantId);

    Task<BrandingAsset?> GetAssetAsync(Guid assetKey, Guid tenantId);
}
