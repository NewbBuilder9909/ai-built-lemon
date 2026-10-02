using Microsoft.Extensions.Options;
using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Tenancy;

public class PlanEntitlementsTests
{
    [Fact]
    public void Enterprise_includes_every_product_feature()
    {
        foreach (var feature in ProductFeature.All)
        {
            Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Enterprise, feature), feature);
        }
    }

    [Fact]
    public void Starter_excludes_contract_ops_and_hub_planner()
    {
        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.ClickUpSync));
        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.ReportingHub));
        Assert.False(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.ContractOps));
        Assert.False(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.HubPlannerSync));
    }

    [Fact]
    public void Professional_adds_hub_planner_and_branding_but_not_contract_ops()
    {
        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Professional, ProductFeature.HubPlannerSync));
        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Professional, ProductFeature.Branding));
        Assert.False(PlanEntitlements.IsEnabled(TenantPlan.Professional, ProductFeature.ContractOps));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Platinum")]
    public void Unknown_or_missing_plan_has_no_features(string? plan)
    {
        Assert.Empty(PlanEntitlements.FeaturesFor(plan));
        Assert.False(PlanEntitlements.IsEnabled(plan, ProductFeature.ClickUpSync));
    }

    [Fact]
    public void Plan_lookup_is_case_insensitive() =>
        Assert.True(PlanEntitlements.IsEnabled("enterprise", ProductFeature.ContractOps));

    [Fact]
    public void Configuration_override_replaces_that_plans_default_set_only()
    {
        var options = new EntitlementOptions();
        options.Plans["Starter"] = [ProductFeature.ContractOps];

        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.ContractOps, options));
        Assert.False(PlanEntitlements.IsEnabled(TenantPlan.Starter, ProductFeature.ClickUpSync, options));
        // Professional wasn't overridden, so its defaults still apply.
        Assert.True(PlanEntitlements.IsEnabled(TenantPlan.Professional, ProductFeature.ClickUpSync, options));
    }

    [Fact]
    public async Task Feature_gate_answers_from_the_resolved_tenants_plan()
    {
        var gate = new TenantFeatureGate(
            FakeTenantContext.ResolvedOn(TenantPlan.Starter),
            new FakeSelections(),
            Options.Create(new EntitlementOptions()));

        Assert.True(await gate.IsEnabledAsync(ProductFeature.ClickUpSync));
        Assert.False(await gate.IsEnabledAsync(ProductFeature.ContractOps));
    }

    [Fact]
    public async Task Feature_gate_is_closed_for_an_unresolved_tenant_and_says_why()
    {
        // Reversed from the original fail-open behaviour (release register
        // R29): a member with no resolvable tenant now gets nothing that is
        // plan-gated, with an outcome that points at account set-up rather
        // than a plan upgrade. See TenantFeatureGateTests for the matrix.
        var gate = new TenantFeatureGate(
            new FakeTenantContext { IsResolved = false },
            new FakeSelections(),
            Options.Create(new EntitlementOptions()));

        var decision = await gate.EvaluateAsync(ProductFeature.ContractOps);

        Assert.False(decision.Enabled);
        Assert.Equal(FeatureGateOutcome.TenantUnresolved, decision.Outcome);
        Assert.False(await gate.IsEnabledAsync(ProductFeature.ContractOps));
    }

    private sealed class FakeSelections(params string[] features) : ITenantFeatureSelectionRepository
    {
        private readonly IReadOnlySet<string> selected = new HashSet<string>(features, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlySet<string>> GetSelectedFeaturesAsync(Guid tenantId) => Task.FromResult(selected);

        public Task ReplaceAsync(Guid tenantId, IEnumerable<string> featureKeys, DateTime nowUtc) => Task.CompletedTask;
    }
}
