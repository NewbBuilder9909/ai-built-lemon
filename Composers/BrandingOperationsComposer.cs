using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.BrandingOps;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Branding Ops domain: the BrandingOps_* schema migration,
/// the profile/asset repository, validation, disk-backed asset storage, and
/// the cached runtime theme resolver. TimeProvider is registered once, by
/// StaffOperationsComposer — deliberately not re-registered here.
/// </summary>
public sealed class BrandingOperationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddMemoryCache();

        builder.Services.AddScoped<IBrandingRepository, BrandingRepository>();
        builder.Services.AddScoped<IBrandingAuditLogRepository, BrandingAuditLogRepository>();
        builder.Services.AddScoped<IBrandingValidationService, BrandingValidationService>();
        builder.Services.AddScoped<IBrandingAssetStorageService, BrandingAssetStorageService>();
        // The product's own customer-facing name (Models/Branding/ProductBrandOptions).
        builder.Services.Configure<ProductBrandOptions>(builder.Config.GetSection(ProductBrandOptions.SectionName));
        builder.Services.AddScoped<IBrandingThemeResolverService, BrandingThemeResolverService>();
        builder.Services.AddScoped<IBrandingAdminService, BrandingAdminService>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, BrandingOpsMigrationStartupHandler>();
    }
}
