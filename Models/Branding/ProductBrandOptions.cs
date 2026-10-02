namespace ProgrammePulse.Models.Branding;

/// <summary>
/// The product's own name, as customers see it — bound from the "Product"
/// configuration section so a rename is one setting, not a code change.
///
/// Why configuration: neither working name is clear. "Hwb" is the Welsh
/// Government's schools platform, and "ProgrammePulse" sits next to
/// BearingPoint's "Program Pulse", a PPM product sold to the same PMO buyer.
/// Until one clears, buyers see the descriptive "Delivery Evidence Check".
/// This is the only product-name setting: /purchase reads it too (it
/// replaced Commercial:ProductName), so no page can disagree with another.
/// Until a name clears trademark, company-name and domain checks
/// (docs/commercial/claim-register.md, "Naming"), nothing customer-facing may
/// hardcode one — views read <see cref="Name"/> instead.
///
/// Not to be confused with a tenant's own branding (BrandingOps), which
/// replaces this inside the signed-in app once a tenant publishes a theme.
/// Code identifiers (namespaces, the Data Protection application name) are
/// deliberately not driven by this: renaming those would break encrypted
/// credentials and cookies, and customers never see them.
/// </summary>
public sealed class ProductBrandOptions
{
    public const string SectionName = "Product";

    public string Name { get; set; } = PlatformDefaultTheme.CompanyName;

    /// <summary>
    /// Trailing part of <see cref="Name"/> the public wordmark shows in the
    /// accent colour (e.g. "Pulse"). Ignored unless it is a proper suffix.
    /// </summary>
    public string? WordmarkAccent { get; set; }

    /// <summary>
    /// True until the name has cleared trademark and domain checks; the public
    /// pages then say it is a working name rather than implying a brand.
    /// </summary>
    public bool IsWorkingName { get; set; }

    public string WordmarkLead => HasAccent ? Name[..^WordmarkAccent!.Length] : Name;

    public string WordmarkAccentPart => HasAccent ? WordmarkAccent! : string.Empty;

    private bool HasAccent =>
        !string.IsNullOrEmpty(WordmarkAccent)
        && WordmarkAccent.Length < Name.Length
        && Name.EndsWith(WordmarkAccent, StringComparison.Ordinal);
}
