using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.Audit;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the cross-area audit trail read (Services/Audit). Its own
/// composer because Services/Audit sits above the feature areas it reads,
/// and none of them may depend on it.
/// </summary>
public sealed class AuditComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IAuditTrailQueryService, AuditTrailQueryService>();
    }
}
