using ProgrammePulse.Data.Dtos;
using ProgrammePulse.Models.Branding;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.BrandingOps;

public sealed class BrandingRepository(IScopeProvider scopeProvider, TimeProvider timeProvider) : IBrandingRepository
{
    public async Task<BrandingProfile?> GetActivePublishedAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Published), tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<BrandingProfile?> GetDraftAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Draft), tenantId));
        return dto is null ? null : Map(dto);
    }

    public async Task<IReadOnlyList<BrandingProfile>> GetHistoryAsync(Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dtos = await scope.Database.FetchAsync<BrandingProfileDto>(
            Sql.Builder
                .Where("(status = @0 OR status = @1) AND tenantId = @2", nameof(BrandingStatus.Published), nameof(BrandingStatus.Archived), tenantId)
                .OrderBy("effectiveFromUtc DESC"));
        return dtos.Select(Map).ToList();
    }

    public async Task<BrandingProfile> SaveDraftAsync(BrandingProfile draft, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Draft), tenantId));

        BrandingProfileDto dto;
        if (existing is not null)
        {
            dto = existing;
            ApplyTokens(dto, draft);
            await scope.Database.UpdateAsync(dto);
        }
        else
        {
            dto = new BrandingProfileDto
            {
                VersionKey = Guid.NewGuid(),
                TenantId = tenantId,
                Status = nameof(BrandingStatus.Draft),
                EffectiveFromUtc = now,
                EffectiveToUtc = null,
                CreatedByMemberId = draft.CreatedByMemberId,
                CreatedAtUtc = now
            };
            ApplyTokens(dto, draft);
            await scope.Database.InsertAsync(dto);
        }

        scope.Complete();
        return Map(dto);
    }

    public async Task<BrandingProfile> PublishAsync(int? actorMemberId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var draftDto = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Draft), tenantId));
        if (draftDto is null)
        {
            throw new InvalidOperationException("There is no draft to publish.");
        }

        var currentPublished = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Published), tenantId));
        if (currentPublished is not null)
        {
            currentPublished.Status = nameof(BrandingStatus.Archived);
            currentPublished.EffectiveToUtc = now;
            await scope.Database.UpdateAsync(currentPublished);
        }

        draftDto.Status = nameof(BrandingStatus.Published);
        draftDto.IsActive = true;
        draftDto.EffectiveFromUtc = now;
        draftDto.EffectiveToUtc = null;
        await scope.Database.UpdateAsync(draftDto);

        scope.Complete();
        return Map(draftDto);
    }

    public async Task<BrandingProfile> RollbackAsync(Guid targetVersionKey, int? actorMemberId, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Scoped to tenantId, not just versionKey — versionKey alone is only
        // globally unique, so without this clause a tenant could roll back
        // to another tenant's archived version.
        var target = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("versionKey = @0 AND tenantId = @1", targetVersionKey, tenantId));
        if (target is null)
        {
            throw new InvalidOperationException($"No branding version found for {targetVersionKey}.");
        }

        var currentPublished = await scope.Database.FirstOrDefaultAsync<BrandingProfileDto>(
            Sql.Builder.Where("status = @0 AND tenantId = @1", nameof(BrandingStatus.Published), tenantId));
        if (currentPublished is not null)
        {
            currentPublished.Status = nameof(BrandingStatus.Archived);
            currentPublished.EffectiveToUtc = now;
            await scope.Database.UpdateAsync(currentPublished);
        }

        var restored = new BrandingProfileDto
        {
            VersionKey = Guid.NewGuid(),
            TenantId = tenantId,
            CompanyName = target.CompanyName,
            TenantUiLabel = target.TenantUiLabel,
            LogoAssetKey = target.LogoAssetKey,
            FaviconAssetKey = target.FaviconAssetKey,
            HeroAssetKey = target.HeroAssetKey,
            PrimaryColour = target.PrimaryColour,
            SecondaryColour = target.SecondaryColour,
            AccentColour = target.AccentColour,
            TextColour = target.TextColour,
            SurfaceColour = target.SurfaceColour,
            BackgroundColour = target.BackgroundColour,
            BorderRadiusPx = target.BorderRadiusPx,
            FontOption = target.FontOption,
            HeaderStyle = target.HeaderStyle,
            FooterStyle = target.FooterStyle,
            DarkModeEnabled = target.DarkModeEnabled,
            IsActive = true,
            Status = nameof(BrandingStatus.Published),
            EffectiveFromUtc = now,
            EffectiveToUtc = null,
            CreatedByMemberId = actorMemberId,
            CreatedAtUtc = now
        };
        await scope.Database.InsertAsync(restored);

        scope.Complete();
        return Map(restored);
    }

    public async Task<BrandingAsset> SaveAssetAsync(BrandingAsset asset, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope();

        var dto = new BrandingAssetDto
        {
            AssetKey = asset.AssetKey,
            TenantId = tenantId,
            AssetType = asset.AssetType.ToString(),
            FileName = asset.FileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            WidthPx = asset.WidthPx,
            HeightPx = asset.HeightPx,
            StoragePath = asset.StoragePath,
            UploadedByMemberId = asset.UploadedByMemberId,
            UploadedAtUtc = asset.UploadedAtUtc
        };
        await scope.Database.InsertAsync(dto);

        scope.Complete();
        return asset with { TenantId = tenantId };
    }

    public async Task<BrandingAsset?> GetAssetAsync(Guid assetKey, Guid tenantId)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = await scope.Database.FirstOrDefaultAsync<BrandingAssetDto>(
            Sql.Builder.Where("assetKey = @0 AND tenantId = @1", assetKey, tenantId));
        return dto is null ? null : MapAsset(dto);
    }

    private static void ApplyTokens(BrandingProfileDto dto, BrandingProfile source)
    {
        dto.CompanyName = source.CompanyName;
        dto.TenantUiLabel = source.TenantUiLabel;
        dto.LogoAssetKey = source.LogoAssetKey;
        dto.FaviconAssetKey = source.FaviconAssetKey;
        dto.HeroAssetKey = source.HeroAssetKey;
        dto.PrimaryColour = source.PrimaryColour;
        dto.SecondaryColour = source.SecondaryColour;
        dto.AccentColour = source.AccentColour;
        dto.TextColour = source.TextColour;
        dto.SurfaceColour = source.SurfaceColour;
        dto.BackgroundColour = source.BackgroundColour;
        dto.BorderRadiusPx = source.BorderRadiusPx;
        dto.FontOption = source.FontOption.ToString();
        dto.HeaderStyle = source.HeaderStyle.ToString();
        dto.FooterStyle = source.FooterStyle.ToString();
        dto.DarkModeEnabled = source.DarkModeEnabled;
        dto.IsActive = source.IsActive;
    }

    private static BrandingProfile Map(BrandingProfileDto dto) => new()
    {
        VersionKey = dto.VersionKey,
        TenantId = dto.TenantId,
        CompanyName = dto.CompanyName,
        TenantUiLabel = dto.TenantUiLabel,
        LogoAssetKey = dto.LogoAssetKey,
        FaviconAssetKey = dto.FaviconAssetKey,
        HeroAssetKey = dto.HeroAssetKey,
        PrimaryColour = dto.PrimaryColour,
        SecondaryColour = dto.SecondaryColour,
        AccentColour = dto.AccentColour,
        TextColour = dto.TextColour,
        SurfaceColour = dto.SurfaceColour,
        BackgroundColour = dto.BackgroundColour,
        BorderRadiusPx = dto.BorderRadiusPx,
        FontOption = Enum.Parse<BrandingFontOption>(dto.FontOption),
        HeaderStyle = Enum.Parse<BrandingHeaderStyle>(dto.HeaderStyle),
        FooterStyle = Enum.Parse<BrandingFooterStyle>(dto.FooterStyle),
        DarkModeEnabled = dto.DarkModeEnabled,
        IsActive = dto.IsActive,
        Status = Enum.Parse<BrandingStatus>(dto.Status),
        EffectiveFromUtc = dto.EffectiveFromUtc,
        EffectiveToUtc = dto.EffectiveToUtc,
        CreatedByMemberId = dto.CreatedByMemberId,
        CreatedAtUtc = dto.CreatedAtUtc
    };

    private static BrandingAsset MapAsset(BrandingAssetDto dto) => new()
    {
        AssetKey = dto.AssetKey,
        TenantId = dto.TenantId,
        AssetType = Enum.Parse<BrandingAssetType>(dto.AssetType),
        FileName = dto.FileName,
        ContentType = dto.ContentType,
        SizeBytes = dto.SizeBytes,
        WidthPx = dto.WidthPx,
        HeightPx = dto.HeightPx,
        StoragePath = dto.StoragePath,
        UploadedByMemberId = dto.UploadedByMemberId,
        UploadedAtUtc = dto.UploadedAtUtc
    };
}
