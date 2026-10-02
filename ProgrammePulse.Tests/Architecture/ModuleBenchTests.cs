using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Keeps the core small. Every /staffops controller is either core (listed
/// here, with the reason it belongs in the product's one loop) or belongs to
/// a benched module and declares [RequireModule]. A new controller fails this
/// test until someone decides which it is.
/// </summary>
public class ModuleBenchTests
{
    private static readonly Dictionary<Type, string> Core = new()
    {
        [typeof(StaffAccountController)] = "Sign-in, password reset and sign-out.",
        [typeof(StaffEvidenceCheckController)] = "Review and History: the check, recorded reviews, decisions and the pack.",
        [typeof(StaffProgrammeOverviewController)] = "Programmes: what the check is about, and sync.",
        [typeof(StaffFileImportController)] = "Data: the credential-free way in.",
        [typeof(StaffSourceConnectionController)] = "Data: connected sources.",
        [typeof(StaffOAuthConnectionController)] = "Data: connected sources that sign in with OAuth.",
        [typeof(StaffIdentityController)] = "Data: matching people, which the check reports on.",
        [typeof(StaffCustomersController)] = "Data: customers, which a review can be scoped to.",
        [typeof(StaffSearchController)] = "Finding a programme, item or person.",
        [typeof(StaffAdminController)] = "Settings: people and access.",
        [typeof(StaffBrandingController)] = "Settings: branding.",
        [typeof(StaffModulesController)] = "Settings: the toolbox that switches modules on.",
        [typeof(StaffPortalController)] = "Landing and start page; its self-service actions are module-gated one by one.",
        [typeof(StaffTenantAdminController)] = "Platform operator console.",
        [typeof(StaffTenantOnboardingController)] = "Platform operator onboarding.",
    };

    private static readonly Dictionary<Type, string> Benched = new()
    {
        [typeof(StaffReportingController)] = ProductModules.Reporting,
        [typeof(StaffRaidController)] = ProductModules.Reporting,
        [typeof(StaffGovernanceController)] = ProductModules.Reporting,
        [typeof(StaffReportingAlertsController)] = ProductModules.Reporting,
        [typeof(StaffJiraTempoController)] = ProductModules.Reporting,
        [typeof(StaffPortfolioAdminController)] = ProductModules.Reporting,
        [typeof(StaffDeliveryLoadController)] = ProductModules.DeliveryLoad,
        [typeof(StaffEstimateCalibrationController)] = ProductModules.EstimateCalibration,
        [typeof(StaffContractController)] = ProductModules.Contracts,
        [typeof(StaffContractAssuranceController)] = ProductModules.Contracts,
        [typeof(StaffCodeRepositoryController)] = ProductModules.Contracts,
        [typeof(StaffSecurityController)] = ProductModules.Contracts,
        [typeof(StaffSkillsController)] = ProductModules.Skills,
        [typeof(StaffContinuityController)] = ProductModules.Skills,
        [typeof(StaffContributionExamplesController)] = ProductModules.Skills,
        [typeof(StaffEvidenceConnectionController)] = ProductModules.Skills,
        [typeof(StaffAzureDevOpsEvidenceController)] = ProductModules.Skills,
        [typeof(StaffServiceController)] = ProductModules.ServiceHealth,
        [typeof(StaffApprovalsController)] = ProductModules.SelfService,
        [typeof(StaffExecutiveReviewController)] = ProductModules.ExecutiveReview,
        [typeof(StaffExecutiveDecisionController)] = ProductModules.ExecutiveReview,
        [typeof(StaffExecutiveDataController)] = ProductModules.ExecutiveReview,
    };

    private static IEnumerable<Type> StaffControllers() =>
        typeof(StaffAdminController).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(StaffAdminController).Namespace
                && t.Name.StartsWith("Staff", StringComparison.Ordinal)
                && typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void Every_staffops_controller_is_either_core_or_on_the_bench()
    {
        var unclassified = StaffControllers().Where(t => !Core.ContainsKey(t) && !Benched.ContainsKey(t)).Select(t => t.Name).ToList();

        Assert.True(unclassified.Count == 0,
            "Decide whether these are core (one loop: scope, import, check, decide, pack) or a benched module: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void Every_benched_controller_declares_its_module()
    {
        foreach (var (controller, module) in Benched)
        {
            var declared = controller.GetCustomAttribute<RequireModuleAttribute>()?.Module;
            Assert.True(declared == module, $"{controller.Name} should declare [RequireModule(\"{module}\")] but declares {declared ?? "nothing"}.");
        }
    }

    [Fact]
    public void No_core_controller_is_benched_as_a_whole()
    {
        var gated = Core.Keys.Where(t => t.GetCustomAttribute<RequireModuleAttribute>() is not null).Select(t => t.Name).ToList();

        Assert.Empty(gated);
    }

    [Fact]
    public void Self_service_actions_are_benched_but_the_landing_and_start_pages_are_not()
    {
        string? ModuleOf(string action) =>
            typeof(StaffPortalController).GetMethods().Where(m => m.Name == action)
                .Select(m => m.GetCustomAttribute<RequireModuleAttribute>()?.Module).FirstOrDefault();

        Assert.Equal(ProductModules.SelfService, ModuleOf(nameof(StaffPortalController.MyWork)));
        Assert.Equal(ProductModules.SelfService, ModuleOf(nameof(StaffPortalController.SubmitLeave)));
        Assert.Null(ModuleOf(nameof(StaffPortalController.Home)));
        Assert.Null(ModuleOf(nameof(StaffPortalController.Start)));
    }

    [Fact]
    public void Every_module_gates_at_least_one_controller()
    {
        var used = Benched.Values.ToHashSet();

        Assert.All(ProductModules.All, m => Assert.Contains(m.Key, used));
    }
}
