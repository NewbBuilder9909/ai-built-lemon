using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Commercial;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Tenancy;

public class SelfServiceSignupServiceTests
{
    private static readonly ICommercialCatalogueService Catalogue =
        new CommercialCatalogueService(
            Options.Create(new CommercialOptions()),
            Options.Create(new ProgrammePulse.Services.Tenancy.EntitlementOptions()));

    [Fact]
    public void Validate_accepts_a_well_formed_self_service_trial()
    {
        var errors = SelfServiceSignupService.Validate(new SelfServiceSignupRequest(
            "Acme Ltd",
            "acme",
            "Alex Admin",
            "alex@example.com",
            "Sup3r-Secret!",
            "Sup3r-Secret!",
            TenantPlan.Starter,
            [ProductFeature.ContractOps]),
            [],
            Catalogue.GetSelfServicePlans());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_non_self_service_plan_and_mismatched_password()
    {
        var errors = SelfServiceSignupService.Validate(new SelfServiceSignupRequest(
            "Acme Ltd",
            "acme",
            "Alex Admin",
            "alex@example.com",
            "one",
            "two",
            TenantPlan.Enterprise,
            []),
            [],
            Catalogue.GetSelfServicePlans());

        Assert.Contains(errors, error => error.Contains("confirmation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("self-service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Catalogue_quote_adds_sellable_modules_on_top_of_the_base_plan()
    {
        var quote = Catalogue.Quote(TenantPlan.Starter, [ProductFeature.ContractOps, ProductFeature.HubPlannerSync]);

        Assert.Equal(895m, quote.MonthlyPrice);
        Assert.Equal(1400m, quote.SetupFee);
        Assert.Contains(ProductFeature.ContractOps, quote.SelectedModules);
        Assert.Contains(ProductFeature.HubPlannerSync, quote.SelectedModules);
        Assert.Contains(ProductFeature.ClickUpSync, quote.EffectiveFeatures);
    }

    [Fact]
    public void Catalogue_ignores_modules_that_are_already_included_in_the_plan()
    {
        var quote = Catalogue.Quote(TenantPlan.Professional, [ProductFeature.Branding, ProductFeature.SupportEvidence]);

        Assert.Equal(995m, quote.MonthlyPrice);
        Assert.Empty(quote.SelectedModules);
    }
}
