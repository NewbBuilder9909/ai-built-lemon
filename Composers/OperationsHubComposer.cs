using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.DemoData;
using ProgrammePulse.Services.Transformation;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Operations Hub's demo data and transformation services.
/// Discovered and run automatically by Umbraco's AddComposers() call in
/// Program.cs — no manual wiring needed there.
/// </summary>
public sealed class OperationsHubComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IDemoErpDataProvider, DemoErpDataProvider>();
        builder.Services.AddScoped<IErpRecordValidator, ErpRecordValidator>();
        builder.Services.AddScoped<IStatusMapper, StatusMapper>();
        builder.Services.AddScoped<IErpTransformationService, ErpTransformationService>();
        builder.Services.AddScoped<IDashboardAggregationService, DashboardAggregationService>();

        // English/Welsh bilingual UI text — see Resources/SharedResource.resx (English)
        // and Resources/SharedResource.cy-GB.resx (Welsh). Culture switching itself
        // (UseRequestLocalization) is configured in Program.cs, since that's
        // request-pipeline middleware rather than a DI service registration.
        //
        // No ResourcesPath is set here deliberately: SharedResource already lives in
        // the ProgrammePulse.Resources namespace, which is exactly the resource
        // base name the default convention resolves to. Setting ResourcesPath="Resources"
        // as well made the factory look for "...Resources.Resources.SharedResource"
        // (doubled), which never matched the actual embedded resource name.
        builder.Services.AddLocalization();
    }
}
