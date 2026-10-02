using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.Tenancy;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Tenancy domain: the Tenancy_Tenant schema migration, the
/// tenant repository, the per-request ITenantContext, plan entitlements
/// (IFeatureGate, [RequireFeature]) and the global TenantAccessFilter that
/// blocks suspended/archived tenants. Discovered and run automatically by
/// AddComposers() in Program.cs, same as every other feature-area composer.
/// </summary>
public sealed class TenancyComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<EntitlementOptions>(builder.Config.GetSection(EntitlementOptions.SectionName));

        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        builder.Services.AddScoped<ITenantFeatureSelectionRepository, TenantFeatureSelectionRepository>();
        builder.Services.AddScoped<ITenantContext, TenantContextAccessor>();
        builder.Services.AddScoped<IFeatureGate, TenantFeatureGate>();
        builder.Services.AddScoped<IModuleSwitchRepository, ModuleSwitchRepository>();
        builder.Services.AddScoped<IModuleGate, ModuleGate>();
        builder.Services.AddScoped<IModuleToolboxService, ModuleToolboxService>();
        builder.Services.AddScoped<ITenantAdminService, TenantAdminService>();
        builder.Services.AddScoped<ITenantAdministratorProvisioningService, TenantAdministratorProvisioningService>();

        // TypeFilter semantics: constructed per request from DI, so the
        // scoped ITenantContext it needs is the same instance the action
        // and layout see.
        builder.Services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add(typeof(TenantAccessFilter));

            // Refuses any action declaring a [CurrentTenant] parameter when
            // no tenant is resolved; ordered after [RequireCapability].
            options.Filters.Add(typeof(CurrentTenantFilter), CurrentTenantFilter.FilterOrder);
            options.Filters.Add(new CrossTenantReferenceExceptionFilter());
        });

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, TenancyMigrationStartupHandler>();
    }
}
