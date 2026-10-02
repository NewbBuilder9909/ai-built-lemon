using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Models.ViewModels.Branding;

namespace ProgrammePulse.Services.BrandingOps;

/// <summary>The branding form as submitted, before validation.</summary>
public sealed record BrandingDraftInput(
    string CompanyName,
    string? TenantUiLabel,
    string PrimaryColour,
    string SecondaryColour,
    string AccentColour,
    string TextColour,
    string SurfaceColour,
    string BackgroundColour,
    int BorderRadiusPx,
    string FontOption,
    string HeaderStyle,
    string FooterStyle,
    bool DarkModeEnabled,
    bool IsActive);

/// <summary>What a branding command did, and the sentence to show afterwards.</summary>
public sealed record BrandingCommandResult(bool Succeeded, string Message);

/// <summary>
/// The branding admin lifecycle: draft, validate, publish, roll back, upload
/// assets, preview. Every change is audited and every publish or rollback
/// invalidates the cached theme. Moved out of StaffBrandingController, so
/// the lifecycle rules sit next to the repository and resolver they
/// coordinate.
/// </summary>
public interface IBrandingAdminService
{
    Task<BrandingAdminViewModel> BuildAdminAsync(Guid tenantId, string? message);

    /// <summary>Null when saved; otherwise the form to re-render with its errors.</summary>
    Task<BrandingAdminViewModel?> SaveDraftAsync(Guid tenantId, BrandingDraftInput input, int? memberId);

    Task<BrandingCommandResult> PublishAsync(Guid tenantId, int? memberId);

    Task<BrandingHistoryViewModel> BuildHistoryAsync(Guid tenantId);

    Task<BrandingCommandResult> RollbackAsync(Guid tenantId, Guid versionKey, int? memberId);

    Task<string> UploadAssetAsync(Guid tenantId, IFormFile file, BrandingAssetType assetType, int? memberId);

    /// <summary>The draft (or else the published theme) resolved for preview; null when neither exists.</summary>
    Task<ResolvedBrandingTheme?> BuildPreviewAsync(Guid tenantId);
}

public sealed class BrandingAdminService(
    IBrandingRepository brandingRepository,
    IBrandingValidationService brandingValidationService,
    IBrandingAssetStorageService brandingAssetStorageService,
    IBrandingThemeResolverService brandingThemeResolverService,
    IBrandingAuditLogRepository brandingAuditLogRepository,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<ProductBrandOptions>? productBrand = null) : IBrandingAdminService
{
    /// <summary>The configured customer-facing product name, used where a tenant has no branding yet.</summary>
    private string ProductName => productBrand?.Value.Name ?? PlatformDefaultTheme.CompanyName;

    public async Task<BrandingAdminViewModel> BuildAdminAsync(Guid tenantId, string? message)
    {
        var draft = await brandingRepository.GetDraftAsync(tenantId);
        var published = await brandingRepository.GetActivePublishedAsync(tenantId);
        var source = draft ?? published;

        var form = source is null
            ? BrandingProfileFormViewModel.Default(ProductName)
            : BrandingProfileFormViewModel.FromProfile(source, await ResolveAssetUrlAsync(source.LogoAssetKey, tenantId), await ResolveAssetUrlAsync(source.FaviconAssetKey, tenantId));

        return new BrandingAdminViewModel(form, published is not null, draft is not null, message, [], []);
    }

    public async Task<BrandingAdminViewModel?> SaveDraftAsync(Guid tenantId, BrandingDraftInput input, int? memberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidate = new BrandingProfile
        {
            VersionKey = Guid.NewGuid(),
            TenantId = tenantId,
            CompanyName = input.CompanyName,
            TenantUiLabel = input.TenantUiLabel,
            PrimaryColour = input.PrimaryColour,
            SecondaryColour = input.SecondaryColour,
            AccentColour = input.AccentColour,
            TextColour = input.TextColour,
            SurfaceColour = input.SurfaceColour,
            BackgroundColour = input.BackgroundColour,
            BorderRadiusPx = input.BorderRadiusPx,
            FontOption = ParseEnumOrInvalid<BrandingFontOption>(input.FontOption),
            HeaderStyle = ParseEnumOrInvalid<BrandingHeaderStyle>(input.HeaderStyle),
            FooterStyle = ParseEnumOrInvalid<BrandingFooterStyle>(input.FooterStyle),
            DarkModeEnabled = input.DarkModeEnabled,
            IsActive = input.IsActive,
            Status = BrandingStatus.Draft,
            EffectiveFromUtc = now,
            EffectiveToUtc = null,
            CreatedByMemberId = memberId,
            CreatedAtUtc = now
        };

        var validation = brandingValidationService.Validate(candidate);
        if (!validation.IsValid)
        {
            var published = await brandingRepository.GetActivePublishedAsync(tenantId);
            return new BrandingAdminViewModel(
                BrandingProfileFormViewModel.FromProfile(candidate, null, null),
                published is not null,
                true,
                null,
                validation.Errors,
                validation.Warnings);
        }

        await brandingRepository.SaveDraftAsync(candidate, tenantId);
        await brandingAuditLogRepository.LogAsync("BrandingProfile", candidate.VersionKey.ToString(), "DraftSaved", memberId, null, now, tenantId);
        return null;
    }

    public async Task<BrandingCommandResult> PublishAsync(Guid tenantId, int? memberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var published = await brandingRepository.PublishAsync(memberId, tenantId);
            brandingThemeResolverService.InvalidateCache(tenantId);
            await brandingAuditLogRepository.LogAsync("BrandingProfile", published.VersionKey.ToString(), "Published", memberId, null, now, tenantId);
            return new BrandingCommandResult(true, "Branding published.");
        }
        catch (InvalidOperationException ex)
        {
            return new BrandingCommandResult(false, ex.Message);
        }
    }

    public async Task<BrandingHistoryViewModel> BuildHistoryAsync(Guid tenantId)
    {
        var versions = await brandingRepository.GetHistoryAsync(tenantId);
        var rows = versions
            .Select(v => new BrandingHistoryRowViewModel(v.VersionKey, v.Status, v.CompanyName, v.PrimaryColour, v.EffectiveFromUtc, v.EffectiveToUtc, v.CreatedByMemberId))
            .ToList();

        return new BrandingHistoryViewModel(rows);
    }

    public async Task<BrandingCommandResult> RollbackAsync(Guid tenantId, Guid versionKey, int? memberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var restored = await brandingRepository.RollbackAsync(versionKey, memberId, tenantId);
            brandingThemeResolverService.InvalidateCache(tenantId);
            await brandingAuditLogRepository.LogAsync(
                "BrandingProfile",
                restored.VersionKey.ToString(),
                "RolledBack",
                memberId,
                JsonSerializer.Serialize(new { restoredFrom = versionKey }),
                now,
                tenantId);
            return new BrandingCommandResult(true, "Rolled back to a previous version.");
        }
        catch (InvalidOperationException ex)
        {
            return new BrandingCommandResult(false, ex.Message);
        }
    }

    public async Task<string> UploadAssetAsync(Guid tenantId, IFormFile file, BrandingAssetType assetType, int? memberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var asset = await brandingAssetStorageService.StoreAsync(file, assetType, memberId, tenantId);
            await AttachAssetToDraftAsync(asset, assetType, memberId, tenantId);
            await brandingAuditLogRepository.LogAsync(
                "BrandingAsset",
                asset.AssetKey.ToString(),
                "AssetUploaded",
                memberId,
                JsonSerializer.Serialize(new { assetType = assetType.ToString(), asset.FileName, asset.SizeBytes }),
                now,
                tenantId);
            return $"{assetType} uploaded.";
        }
        catch (BrandingAssetValidationException ex)
        {
            return ex.Message;
        }
    }

    public async Task<ResolvedBrandingTheme?> BuildPreviewAsync(Guid tenantId)
    {
        var source = await brandingRepository.GetDraftAsync(tenantId) ?? await brandingRepository.GetActivePublishedAsync(tenantId);
        return source is null ? null : await brandingThemeResolverService.ResolvePreviewAsync(source);
    }

    private async Task AttachAssetToDraftAsync(BrandingAsset asset, BrandingAssetType assetType, int? memberId, Guid tenantId)
    {
        var draft = await brandingRepository.GetDraftAsync(tenantId)
            ?? await brandingRepository.GetActivePublishedAsync(tenantId)
            ?? DefaultProfile(memberId, tenantId);

        var updated = draft with
        {
            LogoAssetKey = assetType == BrandingAssetType.Logo ? asset.AssetKey : draft.LogoAssetKey,
            FaviconAssetKey = assetType == BrandingAssetType.Favicon ? asset.AssetKey : draft.FaviconAssetKey,
            HeroAssetKey = assetType == BrandingAssetType.Hero ? asset.AssetKey : draft.HeroAssetKey
        };

        await brandingRepository.SaveDraftAsync(updated, tenantId);
    }

    private BrandingProfile DefaultProfile(int? memberId, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new BrandingProfile
        {
            VersionKey = Guid.NewGuid(),
            TenantId = tenantId,
            CompanyName = ProductName,
            PrimaryColour = PlatformDefaultTheme.PrimaryColour,
            SecondaryColour = PlatformDefaultTheme.SecondaryColour,
            AccentColour = PlatformDefaultTheme.AccentColour,
            TextColour = PlatformDefaultTheme.TextColour,
            SurfaceColour = PlatformDefaultTheme.SurfaceColour,
            BackgroundColour = PlatformDefaultTheme.BackgroundColour,
            BorderRadiusPx = PlatformDefaultTheme.BorderRadiusPx,
            FontOption = PlatformDefaultTheme.FontOption,
            HeaderStyle = PlatformDefaultTheme.HeaderStyle,
            FooterStyle = PlatformDefaultTheme.FooterStyle,
            DarkModeEnabled = PlatformDefaultTheme.DarkModeEnabled,
            IsActive = true,
            Status = BrandingStatus.Draft,
            EffectiveFromUtc = now,
            CreatedByMemberId = memberId,
            CreatedAtUtc = now
        };
    }

    private async Task<string?> ResolveAssetUrlAsync(Guid? assetKey, Guid tenantId)
    {
        if (assetKey is null)
        {
            return null;
        }

        var asset = await brandingRepository.GetAssetAsync(assetKey.Value, tenantId);
        return asset is null ? null : brandingAssetStorageService.ResolveUrl(asset);
    }

    private static TEnum ParseEnumOrInvalid<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, out var parsed) ? parsed : (TEnum)(object)(-1);
}
