using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.ServiceOps;
using ProgrammePulse.Services.Integrations.Freshdesk;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ServiceOps;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Service Ops domain: the ServiceOps_* schema migration,
/// the repository covering its aggregates, the Freshdesk Bronze client
/// and ingestion, the support-to-code link lifecycle, the service health
/// query, and its participation in GDPR export/erasure.
///
/// A composer of its own, not an extension of SkillsEvidenceComposer.
/// The design document promises a customer can connect a support desk
/// without connecting a repository; registering, migrating and failing
/// independently of the evidence area is what makes that structurally
/// true. TimeProvider is registered once, by StaffOperationsComposer.
/// </summary>
public sealed class ServiceOperationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<ServiceOpsOptions>(
            builder.Config.GetSection(ServiceOpsOptions.SectionName));

        builder.Services.AddScoped<IServiceOpsRepository, ServiceOpsRepository>();
        builder.Services.AddSingleton<IDeskCredentialProtector, DeskCredentialProtector>();
        builder.Services.AddScoped<ISupportCodeLinkService, SupportCodeLinkService>();
        builder.Services.AddScoped<IServiceHealthQueryService, ServiceHealthQueryService>();
        builder.Services.AddScoped<FreshdeskIngestionService>();
        builder.Services.AddScoped<IFreshdeskConnectionService, FreshdeskConnectionService>();
        builder.Services.AddScoped<IDeskAdminService, DeskAdminService>();
        builder.Services.AddScoped<ISupportParticipationQueryService, SupportParticipationQueryService>();

        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, ServiceOpsDataParticipant>();

        // No BaseAddress: the host comes from the connection's validated
        // ApiBaseUrl on every request. Freshdesk is per-customer
        // subdomain, so a pooled default would be wrong for every tenant
        // but one.
        builder.Services.AddHttpClient<IFreshdeskClient, FreshdeskClient>(
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, ServiceOpsMigrationStartupHandler>();
    }
}
