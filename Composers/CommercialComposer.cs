using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.Commercial;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the public commercial surface at /purchase. Its own composer
/// rather than a line in an existing one, following the one-composer-per-
/// feature-area rule — this is the only part of the application reachable
/// without authentication, so keeping its wiring separate makes that
/// boundary easy to audit. No repositories, no migrations, no tenant
/// context: the page is configuration and static content only.
/// </summary>
public sealed class CommercialComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<CommercialOptions>(builder.Config.GetSection(CommercialOptions.SectionName));
        builder.Services.AddSingleton<ICommercialCatalogueService, CommercialCatalogueService>();
        builder.Services.AddScoped<ITenantModuleService, TenantModuleService>();
        builder.Services.AddScoped<ISelfServiceSignupService, SelfServiceSignupService>();
    }
}
