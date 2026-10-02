using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Optional per-deployment override of the default plan → feature matrix,
/// bound from configuration section "Entitlements":
/// <code>
/// "Entitlements": { "Plans": { "Starter": [ "ClickUpSync", "ReportingHub" ] } }
/// </code>
/// A plan listed here replaces that plan's default set entirely; plans not
/// listed keep <see cref="PlanEntitlements.Defaults"/>.
/// </summary>
public sealed class EntitlementOptions
{
    public const string SectionName = "Entitlements";

    public Dictionary<string, string[]> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Which <see cref="ProductFeature"/> keys each <see cref="TenantPlan"/>
/// includes. Pure lookup; TenantFeatureGate applies it to the current
/// tenant. An unknown plan name has no features — safer than guessing.
/// </summary>
public static class PlanEntitlements
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Defaults =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [TenantPlan.Starter] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ProductFeature.FileImport,
                ProductFeature.ClickUpSync,
                ProductFeature.ReportingHub
            },
            [TenantPlan.Professional] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ProductFeature.FileImport,
                ProductFeature.ClickUpSync,
                ProductFeature.ReportingHub,
                ProductFeature.HubPlannerSync,
                ProductFeature.Branding,
                // Service health is case metadata about a product, not
                // person-level data, so it does not carry the privacy
                // weight that keeps GitHubEvidence to Enterprise.
                ProductFeature.SupportEvidence
            },
            [TenantPlan.Enterprise] = new HashSet<string>(ProductFeature.All, StringComparer.OrdinalIgnoreCase)
        };

    public static IReadOnlySet<string> FeaturesFor(string? plan, EntitlementOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(plan))
        {
            return new HashSet<string>();
        }

        if (options is not null && options.Plans.TryGetValue(plan, out var configured))
        {
            return new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase);
        }

        return Defaults.TryGetValue(plan, out var defaults) ? defaults : new HashSet<string>();
    }

    public static bool IsEnabled(string? plan, string feature, EntitlementOptions? options = null) =>
        FeaturesFor(plan, options).Contains(feature);
}
