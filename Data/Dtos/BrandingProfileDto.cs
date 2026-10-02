using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace ProgrammePulse.Data.Dtos;

/// <summary>
/// One row per branding version. Lifecycle is Draft -&gt; Published -&gt; Archived
/// (status column) with effectiveFromUtc/effectiveToUtc closing out
/// superseded rows — same append-only pattern as StaffRateDto. Nothing
/// outside Services/BrandingOps should reference this Dto.
/// </summary>
[TableName(TableName)]
[ExplicitColumns]
internal sealed class BrandingProfileDto
{
    public const string TableName = "BrandingOps_Profile";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("versionKey")]
    [Index(IndexTypes.UniqueNonClustered)]
    public Guid VersionKey { get; set; }

    [Column("tenantId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? TenantId { get; set; }

    [Column("companyName")]
    [Length(200)]
    public string CompanyName { get; set; } = null!;

    [Column("tenantUiLabel")]
    [Length(200)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? TenantUiLabel { get; set; }

    [Column("logoAssetKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? LogoAssetKey { get; set; }

    [Column("faviconAssetKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? FaviconAssetKey { get; set; }

    [Column("heroAssetKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? HeroAssetKey { get; set; }

    [Column("primaryColour")]
    [Length(7)]
    public string PrimaryColour { get; set; } = null!;

    [Column("secondaryColour")]
    [Length(7)]
    public string SecondaryColour { get; set; } = null!;

    [Column("accentColour")]
    [Length(7)]
    public string AccentColour { get; set; } = null!;

    [Column("textColour")]
    [Length(7)]
    public string TextColour { get; set; } = null!;

    [Column("surfaceColour")]
    [Length(7)]
    public string SurfaceColour { get; set; } = null!;

    [Column("backgroundColour")]
    [Length(7)]
    public string BackgroundColour { get; set; } = null!;

    [Column("borderRadiusPx")]
    public int BorderRadiusPx { get; set; }

    [Column("fontOption")]
    [Length(32)]
    public string FontOption { get; set; } = null!;

    [Column("headerStyle")]
    [Length(32)]
    public string HeaderStyle { get; set; } = null!;

    [Column("footerStyle")]
    [Length(32)]
    public string FooterStyle { get; set; } = null!;

    [Column("darkModeEnabled")]
    public bool DarkModeEnabled { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }

    [Column("status")]
    [Length(16)]
    public string Status { get; set; } = null!;

    [Column("effectiveFromUtc")]
    public DateTime EffectiveFromUtc { get; set; }

    [Column("effectiveToUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? EffectiveToUtc { get; set; }

    [Column("createdByMemberId")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? CreatedByMemberId { get; set; }

    [Column("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }
}
