using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.BrandingOps;

/// <summary>
/// In-memory stand-in for IBrandingRepository, following the hand-rolled
/// fake convention used elsewhere in this test project (see
/// ProgrammeOps/FakeProgrammeRepository) rather than a mocking library.
/// Mirrors the real repository's Draft/Published/Archived lifecycle rules
/// closely enough to exercise callers, without touching NPoco. Every method
/// filters by tenantId, same as the real repository, so tests can assert
/// tenant isolation the same way the real schema enforces it.
/// </summary>
public sealed class FakeBrandingRepository : IBrandingRepository
{
    public readonly List<BrandingProfile> Profiles = [];
    public readonly List<BrandingAsset> Assets = [];
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public Task<BrandingProfile?> GetActivePublishedAsync(Guid tenantId) =>
        Task.FromResult(Profiles.FirstOrDefault(p => p.Status == BrandingStatus.Published && p.TenantId == tenantId));

    public Task<BrandingProfile?> GetDraftAsync(Guid tenantId) =>
        Task.FromResult(Profiles.FirstOrDefault(p => p.Status == BrandingStatus.Draft && p.TenantId == tenantId));

    public Task<IReadOnlyList<BrandingProfile>> GetHistoryAsync(Guid tenantId) =>
        Task.FromResult<IReadOnlyList<BrandingProfile>>(
            Profiles.Where(p => p.Status is BrandingStatus.Published or BrandingStatus.Archived && p.TenantId == tenantId)
                .OrderByDescending(p => p.EffectiveFromUtc)
                .ToList());

    public Task<BrandingProfile> SaveDraftAsync(BrandingProfile draft, Guid tenantId)
    {
        var existing = Profiles.FirstOrDefault(p => p.Status == BrandingStatus.Draft && p.TenantId == tenantId);
        if (existing is not null)
        {
            Profiles.Remove(existing);
            draft = draft with { VersionKey = existing.VersionKey, CreatedAtUtc = existing.CreatedAtUtc, CreatedByMemberId = existing.CreatedByMemberId };
        }

        draft = draft with { Status = BrandingStatus.Draft, TenantId = tenantId };
        Profiles.Add(draft);
        return Task.FromResult(draft);
    }

    public Task<BrandingProfile> PublishAsync(int? actorMemberId, Guid tenantId)
    {
        var draft = Profiles.FirstOrDefault(p => p.Status == BrandingStatus.Draft && p.TenantId == tenantId)
            ?? throw new InvalidOperationException("There is no draft to publish.");

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        ArchiveCurrentPublished(tenantId, now);

        Profiles.Remove(draft);
        var published = draft with { Status = BrandingStatus.Published, IsActive = true, EffectiveFromUtc = now, EffectiveToUtc = null };
        Profiles.Add(published);
        return Task.FromResult(published);
    }

    public Task<BrandingProfile> RollbackAsync(Guid targetVersionKey, int? actorMemberId, Guid tenantId)
    {
        var target = Profiles.FirstOrDefault(p => p.VersionKey == targetVersionKey && p.TenantId == tenantId)
            ?? throw new InvalidOperationException($"No branding version found for {targetVersionKey}.");

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        ArchiveCurrentPublished(tenantId, now);

        var restored = target with
        {
            VersionKey = Guid.NewGuid(),
            TenantId = tenantId,
            Status = BrandingStatus.Published,
            IsActive = true,
            EffectiveFromUtc = now,
            EffectiveToUtc = null,
            CreatedByMemberId = actorMemberId,
            CreatedAtUtc = now
        };
        Profiles.Add(restored);
        return Task.FromResult(restored);
    }

    public Task<BrandingAsset> SaveAssetAsync(BrandingAsset asset, Guid tenantId)
    {
        asset = asset with { TenantId = tenantId };
        Assets.Add(asset);
        return Task.FromResult(asset);
    }

    public Task<BrandingAsset?> GetAssetAsync(Guid assetKey, Guid tenantId) =>
        Task.FromResult(Assets.FirstOrDefault(a => a.AssetKey == assetKey && a.TenantId == tenantId));

    private void ArchiveCurrentPublished(Guid tenantId, DateTime now)
    {
        var current = Profiles.FirstOrDefault(p => p.Status == BrandingStatus.Published && p.TenantId == tenantId);
        if (current is null)
        {
            return;
        }

        Profiles.Remove(current);
        Profiles.Add(current with { Status = BrandingStatus.Archived, EffectiveToUtc = now });
    }
}
