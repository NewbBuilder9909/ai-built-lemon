using System.Text.Json;
using ProgrammePulse.Models.ViewModels.Tenancy;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.Commercial;

/// <summary>
/// A tenant's sellable add-on modules and what its plan and modules cost,
/// for the Platform Admin's tenant console.
///
/// Lives in Commercial, not Tenancy, because pricing is Commercial's and
/// Commercial already depends on Tenancy: putting this in TenantAdminService
/// would make the two areas depend on each other (FeatureAreaDependencyTests).
/// The controller composes the two, the same way it composes sync status.
/// </summary>
public interface ITenantModuleService
{
    IReadOnlyList<TenantSellableModuleViewModel> SellableModules();

    /// <summary>Adds each tenant's selected modules, effective features and price to rows built by Tenancy.</summary>
    Task<IReadOnlyList<TenantAdminRowViewModel>> WithCommercialTermsAsync(IReadOnlyList<TenantAdminRowViewModel> rows);

    /// <summary>Replaces a tenant's modules, keeping only those its plan may buy. Null if the tenant doesn't exist.</summary>
    Task<string?> SetModulesAsync(Guid tenantKey, IEnumerable<string>? requestedModules, int? actorMemberId);

    /// <summary>After a plan change, drops modules the new plan can't hold.</summary>
    Task NormaliseForPlanAsync(Guid tenantKey, string plan);
}

public sealed class TenantModuleService(
    ICommercialCatalogueService catalogue,
    ITenantRepository tenants,
    ITenantFeatureSelectionRepository selections,
    IStaffAuditLogRepository audit,
    TimeProvider timeProvider) : ITenantModuleService
{
    public IReadOnlyList<TenantSellableModuleViewModel> SellableModules() =>
        catalogue.GetModules()
            .Where(module => module.SelfServiceAvailable)
            .Select(module => new TenantSellableModuleViewModel(module.FeatureKey, module.DisplayName, module.MonthlyPrice, module.SetupFee))
            .ToList();

    public async Task<IReadOnlyList<TenantAdminRowViewModel>> WithCommercialTermsAsync(IReadOnlyList<TenantAdminRowViewModel> rows)
    {
        var priced = new List<TenantAdminRowViewModel>(rows.Count);
        foreach (var row in rows)
        {
            var quote = catalogue.Quote(row.Plan, await selections.GetSelectedFeaturesAsync(row.TenantKey));
            priced.Add(row with
            {
                Features = quote.EffectiveFeatures,
                SelectedModules = quote.SelectedModules,
                MonthlyPrice = quote.MonthlyPrice,
                SetupFee = quote.SetupFee,
            });
        }

        return priced;
    }

    public async Task<string?> SetModulesAsync(Guid tenantKey, IEnumerable<string>? requestedModules, int? actorMemberId)
    {
        var tenant = await tenants.GetByKeyAsync(tenantKey);
        if (tenant is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var modules = catalogue.NormalizeSelfServiceModules(tenant.Plan, requestedModules ?? []);
        await selections.ReplaceAsync(tenantKey, modules, now);
        await audit.LogAsync("Tenant", tenantKey.ToString(), "TenantModulesChanged", actorMemberId,
            JsonSerializer.Serialize(new { modules = modules.Order(StringComparer.Ordinal).ToList() }), now, tenantKey);

        return $"'{tenant.Name}' modules updated.";
    }

    public async Task NormaliseForPlanAsync(Guid tenantKey, string plan)
    {
        var existing = await selections.GetSelectedFeaturesAsync(tenantKey);
        await selections.ReplaceAsync(tenantKey, catalogue.NormalizeSelfServiceModules(plan, existing), timeProvider.GetUtcNow().UtcDateTime);
    }
}
