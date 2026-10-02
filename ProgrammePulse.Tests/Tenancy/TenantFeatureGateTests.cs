using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Tenancy;

/// <summary>
/// The gate's decision step. The important case is the first one: an
/// unresolved tenant is answered closed (release register R29 / GTM review
/// B08) — the previous behaviour granted every plan-gated feature to any
/// member whose staff row had no tenant.
/// </summary>
public class TenantFeatureGateTests
{
    private sealed class FakeSelections(params string[] features) : ITenantFeatureSelectionRepository
    {
        private readonly IReadOnlySet<string> selected = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlySet<string>> GetSelectedFeaturesAsync(Guid tenantId) => Task.FromResult(selected);

        public Task ReplaceAsync(Guid tenantId, IEnumerable<string> featureKeys, DateTime nowUtc) => Task.CompletedTask;
    }

    private static Tenant TenantOn(string plan) => new()
    {
        TenantKey = Guid.NewGuid(),
        Name = "Acme",
        ShortCode = "acme",
        IsActive = true,
        Status = TenantStatus.Active,
        Plan = plan,
        CreatedAtUtc = new DateTime(2026, 1, 1)
    };

    [Fact]
    public void An_unresolved_tenant_is_refused_with_the_setup_outcome_not_the_plan_outcome()
    {
        var decision = TenantFeatureGate.Decide(null, ProductFeature.ClickUpSync, null, null);

        Assert.False(decision.Enabled);
        Assert.Equal(FeatureGateOutcome.TenantUnresolved, decision.Outcome);
        Assert.Null(decision.Plan);
    }

    [Fact]
    public void A_plan_without_the_feature_is_refused_with_the_plan_outcome()
    {
        var decision = TenantFeatureGate.Decide(TenantOn(TenantPlan.Starter), ProductFeature.HubPlannerSync, null, null);

        Assert.False(decision.Enabled);
        Assert.Equal(FeatureGateOutcome.NotInPlan, decision.Outcome);
        Assert.Equal(TenantPlan.Starter, decision.Plan);
    }

    [Fact]
    public void A_plan_with_the_feature_is_enabled()
    {
        var decision = TenantFeatureGate.Decide(TenantOn(TenantPlan.Professional), ProductFeature.HubPlannerSync, null, null);

        Assert.True(decision.Enabled);
        Assert.Equal(FeatureGateOutcome.Enabled, decision.Outcome);
    }

    [Fact]
    public void Configured_entitlements_override_the_defaults()
    {
        var options = new EntitlementOptions { Plans = { [TenantPlan.Starter] = [ProductFeature.HubPlannerSync] } };

        Assert.True(TenantFeatureGate.Decide(TenantOn(TenantPlan.Starter), ProductFeature.HubPlannerSync, null, options).Enabled);
        Assert.False(TenantFeatureGate.Decide(TenantOn(TenantPlan.Starter), ProductFeature.ClickUpSync, null, options).Enabled);
    }

    [Fact]
    public void A_tenant_add_on_enables_a_feature_even_when_the_plan_does_not_include_it()
    {
        var decision = TenantFeatureGate.Decide(
            TenantOn(TenantPlan.Starter),
            ProductFeature.ContractOps,
            new HashSet<string>([ProductFeature.ContractOps], StringComparer.OrdinalIgnoreCase),
            null);

        Assert.True(decision.Enabled);
        Assert.Equal(FeatureGateOutcome.Enabled, decision.Outcome);
    }

    [Fact]
    public async Task Runtime_gate_reads_the_selected_tenant_add_ons()
    {
        var gate = new TenantFeatureGate(
            FakeTenantContext.ResolvedOn(TenantPlan.Starter),
            new FakeSelections(ProductFeature.ContractOps),
            Microsoft.Extensions.Options.Options.Create(new EntitlementOptions()));

        Assert.True(await gate.IsEnabledAsync(ProductFeature.ContractOps));
    }
}
