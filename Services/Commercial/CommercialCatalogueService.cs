using Microsoft.Extensions.Options;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.Commercial;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.Commercial;

public interface ICommercialCatalogueService
{
    IReadOnlyList<CommercialPlanViewModel> GetPlans();

    IReadOnlyList<CommercialModuleViewModel> GetModules();

    IReadOnlyList<string> GetSelfServicePlans();

    IReadOnlySet<string> NormalizeSelfServiceModules(string? plan, IEnumerable<string>? requestedModules);

    CommercialQuoteViewModel Quote(string? plan, IEnumerable<string>? requestedModules);
}

public sealed class CommercialCatalogueService(
    IOptions<CommercialOptions> options,
    IOptions<EntitlementOptions> entitlementOptions) : ICommercialCatalogueService
{
    private static readonly IReadOnlyDictionary<string, int> PlanOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        [TenantPlan.Starter] = 0,
        [TenantPlan.Professional] = 1,
        [TenantPlan.Enterprise] = 2
    };

    public IReadOnlyList<CommercialPlanViewModel> GetPlans() =>
        TenantPlan.All.Select(plan =>
        {
            var offer = PlanOffer(plan);
            return new CommercialPlanViewModel(
                plan,
                offer.DisplayName,
                offer.MonthlyPrice,
                offer.SetupFee,
                offer.Summary,
                offer.SelfServiceAvailable,
                PlanEntitlements.FeaturesFor(plan, entitlementOptions.Value).OrderBy(feature => feature).ToList());
        }).ToList();

    public IReadOnlyList<CommercialModuleViewModel> GetModules() =>
        options.Value.Modules
            .OrderBy(kvp => kvp.Value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new CommercialModuleViewModel(
                kvp.Key,
                kvp.Value.DisplayName,
                kvp.Value.MonthlyPrice,
                kvp.Value.SetupFee,
                kvp.Value.Summary,
                kvp.Value.SelfServiceAvailable,
                kvp.Value.AvailableFromPlan))
            .ToList();

    public IReadOnlyList<string> GetSelfServicePlans() =>
        TenantPlan.All.Where(plan => PlanOffer(plan).SelfServiceAvailable).ToList();

    public IReadOnlySet<string> NormalizeSelfServiceModules(string? plan, IEnumerable<string>? requestedModules)
    {
        if (!TenantPlan.IsKnown(plan))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedPlan = TenantPlan.Normalize(plan!);
        var included = PlanEntitlements.FeaturesFor(normalizedPlan, entitlementOptions.Value);
        var requested = requestedModules ?? [];

        return requested
            .Where(feature => ModuleOffer(feature) is { SelfServiceAvailable: true } offer
                && IsAvailableOnPlan(offer, normalizedPlan)
                && !included.Contains(feature))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public CommercialQuoteViewModel Quote(string? plan, IEnumerable<string>? requestedModules)
    {
        var normalizedPlan = TenantPlan.IsKnown(plan) ? TenantPlan.Normalize(plan!) : null;
        var selectedModules = normalizedPlan is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : NormalizeSelfServiceModules(normalizedPlan, requestedModules);
        var included = normalizedPlan is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(PlanEntitlements.FeaturesFor(normalizedPlan, entitlementOptions.Value), StringComparer.OrdinalIgnoreCase);

        foreach (var feature in selectedModules)
        {
            included.Add(feature);
        }

        var monthly = normalizedPlan is null ? 0m : PlanOffer(normalizedPlan).MonthlyPrice;
        var setup = normalizedPlan is null ? 0m : PlanOffer(normalizedPlan).SetupFee;

        foreach (var feature in selectedModules)
        {
            var offer = ModuleOffer(feature);
            if (offer is null)
            {
                continue;
            }

            monthly += offer.MonthlyPrice;
            setup += offer.SetupFee;
        }

        return new CommercialQuoteViewModel(
            normalizedPlan,
            monthly,
            setup,
            selectedModules.OrderBy(feature => feature).ToList(),
            included.OrderBy(feature => feature).ToList());
    }

    private CommercialOptions.PlanOffer PlanOffer(string plan) =>
        options.Value.Plans.TryGetValue(plan, out var offer)
            ? offer
            : new CommercialOptions.PlanOffer { DisplayName = plan };

    private CommercialOptions.ModuleOffer? ModuleOffer(string feature) =>
        options.Value.Modules.TryGetValue(feature, out var offer) ? offer : null;

    private static bool IsAvailableOnPlan(CommercialOptions.ModuleOffer offer, string plan) =>
        string.IsNullOrWhiteSpace(offer.AvailableFromPlan)
        || (PlanOrder.TryGetValue(plan, out var current)
            && PlanOrder.TryGetValue(offer.AvailableFromPlan, out var minimum)
            && current >= minimum);
}
