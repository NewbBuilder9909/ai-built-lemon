using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.SecurityAssurance;
using ProgrammePulse.Services.Integrations;
using ProgrammePulse.Services.Integrations.Aikido;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.SecurityAssurance;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Security Assurance area: the SecurityAssurance_* schema,
/// its repository, the credential protector, the tool-neutral snapshot that
/// Contract Ops reads, and Aikido as the first scanning tool.
///
/// Its own composer and migration plan, so a customer can use contract
/// assurance without connecting repository evidence (the Service Ops
/// precedent). TimeProvider and SyncRunGuard are registered elsewhere.
/// </summary>
public sealed class SecurityAssuranceComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<ISecurityAssuranceRepository, SecurityAssuranceRepository>();
        builder.Services.AddSingleton<ISecurityCredentialProtector, SecurityCredentialProtector>();
        builder.Services.AddScoped<ISecurityAssuranceQueryService, SecurityAssuranceQueryService>();
        builder.Services.AddScoped<IAikidoConnectionService, AikidoConnectionService>();
        builder.Services.AddScoped<AikidoIngestionService>();

        // No BaseAddress: the host comes from the connection's region, checked
        // against AikidoRegions on every request. No redirects, no cookies.
        builder.Services.AddHttpClient<IAikidoClient, AikidoClient>(client => client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, SecurityAssuranceMigrationStartupHandler>();
    }
}
