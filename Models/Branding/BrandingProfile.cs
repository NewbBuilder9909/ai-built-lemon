namespace ProgrammePulse.Models.Branding;

/// <summary>
/// A single version of the site's branding configuration. Lifecycle is
/// Draft -&gt; Published -&gt; Archived on this same shape (see BrandingStatus) —
/// there is deliberately no separate "ThemeVersion" entity; a version *is* a
/// BrandingProfile row with EffectiveFromUtc/EffectiveToUtc set, the same
/// append-only pattern as Models.Staff.StaffRate.
/// </summary>
public sealed record BrandingProfile
{
    public required Guid VersionKey { get; init; }

    /// <summary>
    /// Null only transiently while a row is constructed before the
    /// repository assigns it — every persisted row carries the tenant that
    /// owns it. See Services/Tenancy/ITenantContext.
    /// </summary>
    public Guid? TenantId { get; init; }

    public required string CompanyName { get; init; }

    public string? TenantUiLabel { get; init; }

    public Guid? LogoAssetKey { get; init; }

    public Guid? FaviconAssetKey { get; init; }

    public Guid? HeroAssetKey { get; init; }

    public required string PrimaryColour { get; init; }

    public required string SecondaryColour { get; init; }

    public required string AccentColour { get; init; }

    public required string TextColour { get; init; }

    public required string SurfaceColour { get; init; }

    public required string BackgroundColour { get; init; }

    public required int BorderRadiusPx { get; init; }

    public required BrandingFontOption FontOption { get; init; }

    public required BrandingHeaderStyle HeaderStyle { get; init; }

    public required BrandingFooterStyle FooterStyle { get; init; }

    public required bool DarkModeEnabled { get; init; }

    public required bool IsActive { get; init; }

    public required BrandingStatus Status { get; init; }

    public required DateTime EffectiveFromUtc { get; init; }

    public DateTime? EffectiveToUtc { get; init; }

    public int? CreatedByMemberId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
