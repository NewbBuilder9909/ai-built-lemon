using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

public sealed class TenantFeatureGate(
    ITenantContext tenantContext,
    ITenantFeatureSelectionRepository tenantFeatureSelectionRepository,
    IOptions<EntitlementOptions> options) : IFeatureGate
{
    public async Task<FeatureGateDecision> EvaluateAsync(string feature)
    {
        await tenantContext.EnsureResolvedAsync();
        var tenant = tenantContext.IsResolved ? tenantContext.CurrentTenant : null;
        var selectedFeatures = tenantContext.IsResolved && tenantContext.CurrentTenantId is { } tenantId
            ? await tenantFeatureSelectionRepository.GetSelectedFeaturesAsync(tenantId)
            : null;
        return Decide(tenant, feature, selectedFeatures, options.Value);
    }

    public async Task<bool> IsEnabledAsync(string feature) => (await EvaluateAsync(feature)).Enabled;

    /// <summary>Pure decision step, unit-tested directly (TenantFeatureGateTests).</summary>
    public static FeatureGateDecision Decide(
        Tenant? tenant,
        string feature,
        IReadOnlySet<string>? selectedFeatures,
        EntitlementOptions? entitlements)
    {
        if (tenant is null)
        {
            return FeatureGateDecision.Unresolved;
        }

        var enabled = PlanEntitlements.IsEnabled(tenant.Plan, feature, entitlements)
            || (selectedFeatures?.Contains(feature) ?? false);
        return new FeatureGateDecision(enabled, enabled ? FeatureGateOutcome.Enabled : FeatureGateOutcome.NotInPlan, tenant.Plan);
    }
}
