using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.ProgrammeOps;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.Integrations;
using ProgrammePulse.Services.Integrations.ClickUp;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Integrations.HubPlanner;
using ProgrammePulse.Services.Integrations.Jira;
using ProgrammePulse.Services.Integrations.Tempo;
using ProgrammePulse.Services.Integrations.OAuth;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Programme Ops domain: the ProgrammeOps_* schema migration,
/// Silver repositories, the ClickUp and Hub Planner Bronze integrations
/// (typed HttpClients with the shared transient-retry handler, plus
/// sync/mapping services), the sync concurrency guard, and the Gold overview
/// query service. TimeProvider is registered once, by StaffOperationsComposer
/// — deliberately not re-registered here.
/// </summary>
public sealed class ProgrammeOperationsComposer : IComposer
{
    private const string SafePlaceholderBaseAddress = "https://example.invalid/";
    private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(30);

    internal static Uri CreateSafeBaseAddress(string? baseUrl, string provider)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new Uri(SafePlaceholderBaseAddress, UriKind.Absolute);
        }

        var trimmed = baseUrl.TrimEnd('/');
        return new Uri(trimmed + "/", UriKind.Absolute);
    }

    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<ProgrammeOpsOptions>(builder.Config.GetSection(ProgrammeOpsOptions.SectionName));
        builder.Services.Configure<ClickUpOptions>(builder.Config.GetSection(ClickUpOptions.SectionName));
        builder.Services.Configure<HubPlannerOptions>(builder.Config.GetSection(HubPlannerOptions.SectionName));
        builder.Services.Configure<ConnectorOAuthOptions>(builder.Config.GetSection(ConnectorOAuthOptions.SectionName));
        builder.Services.AddOptions<TempoReconciliationOptions>().Bind(builder.Config.GetSection(TempoReconciliationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.EnabledTenantIds is not null && o.EnabledTenantIds.All(id => id != Guid.Empty)
                && o.EnabledTenantIds.Distinct().Count() == o.EnabledTenantIds.Length, "Tempo reconciliation requires distinct, nonempty tenant IDs.")
            .ValidateOnStart();
        builder.Services.AddHttpClient<ConnectorOAuthClient>(client => client.Timeout = UpstreamTimeout)
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler);
        builder.Services.AddScoped<ConnectorAccessTokenService>();
        builder.Services.AddHttpClient<JiraApiClient>(client => { client.BaseAddress = new Uri("https://api.atlassian.com/"); client.Timeout = UpstreamTimeout; })
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();
        builder.Services.AddHttpClient<TempoApiClient>(client => { client.BaseAddress = new Uri("https://api.tempo.io/"); client.Timeout = UpstreamTimeout; })
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        // Transient (per-client-instance) so each typed client gets its own
        // handler instance in its pipeline — the handler holds no state.
        builder.Services.AddTransient<TransientHttpRetryHandler>();
        builder.Services.AddSingleton<SyncRunGuard>();

        // Encrypts a tenant's own ClickUp/Hub Planner credential at rest —
        // see ISourceCredentialProtector. AddDataProtection() is safe to
        // call even if something else in the pipeline already registered
        // the default key ring.
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<ISourceCredentialProtector, SourceCredentialProtector>();

        builder.Services.AddHttpClient<IClickUpApiClient, ClickUpApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClickUpOptions>>().Value;
                client.BaseAddress = CreateSafeBaseAddress(options.BaseUrl, "ClickUp");
                client.Timeout = UpstreamTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        builder.Services.AddHttpClient<IHubPlannerApiClient, HubPlannerApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HubPlannerOptions>>().Value;
                client.BaseAddress = CreateSafeBaseAddress(options.BaseUrl, "HubPlanner");
                client.Timeout = UpstreamTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        builder.Services.AddScoped<IProgrammeRepository, ProgrammeRepository>();
        // Same scoped instance: query services take the read half only.
        builder.Services.AddScoped<IProgrammeReadRepository>(sp => sp.GetRequiredService<IProgrammeRepository>());
        builder.Services.AddScoped<IRawConnectorPayloadRepository, RawConnectorPayloadRepository>();
        builder.Services.AddScoped<IImportStagingRepository, ImportStagingRepository>();
        builder.Services.AddScoped<IEstimateBaselineRepository, EstimateBaselineRepository>();
        builder.Services.AddScoped<EstimateCalibrationService>();
        builder.Services.AddScoped<IDeliveryLoadQueryService, DeliveryLoadQueryService>();
        builder.Services.AddScoped<ISourceConnectionRepository, SourceConnectionRepository>();
        builder.Services.AddScoped<IClickUpRawPayloadRepository, ClickUpRawPayloadRepository>();
        builder.Services.AddScoped<IHubPlannerRawPayloadRepository, HubPlannerRawPayloadRepository>();
        builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        builder.Services.AddScoped<IStaffRoleAssignmentService, StaffRoleAssignmentService>();

        // Identity resolution (explicit links + unresolved queue) and durable
        // sync-run state. The resolver is scoped so one sync run loads staff
        // and links once; the coordinator is scoped because its repository
        // is, and takes the singleton SyncRunGuard as the in-process latch.
        builder.Services.AddScoped<IIdentityResolutionRepository, IdentityResolutionRepository>();
        builder.Services.AddScoped<IStaffIdentityResolver, StaffIdentityResolver>();
        builder.Services.AddScoped<ISyncRunRepository, SyncRunRepository>();
        builder.Services.AddScoped<SyncRunCoordinator>();
        builder.Services.AddScoped<ISyncStatusQueryService, SyncStatusQueryService>();
        builder.Services.AddScoped<IEvidenceCheckService, EvidenceCheckService>();
        builder.Services.AddScoped<IJiraTempoReconciliationQueryService, JiraTempoReconciliationQueryService>();
        builder.Services.AddScoped<IEvidenceReviewRepository, EvidenceReviewRepository>();
        builder.Services.AddScoped<IEvidenceReviewService, EvidenceReviewService>();
        builder.Services.AddScoped<IDeliveryDataPurgeRepository, DeliveryDataPurgeRepository>();
        builder.Services.AddScoped<IDeliveryDataPurgeService, DeliveryDataPurgeService>();
        builder.Services.AddScoped<IIntegrationRunStatusQueryService, IntegrationRunStatusQueryService>();
        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, IdentityLinkDataParticipant>();
        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, EvidenceReviewDataParticipant>();

        builder.Services.AddScoped<IClickUpStatusMapper, ClickUpStatusMapper>();
        builder.Services.AddScoped<IClickUpMappingService, ClickUpMappingService>();
        builder.Services.AddScoped<IClickUpSyncService, ClickUpSyncService>();

        builder.Services.AddScoped<IHubPlannerMappingService, HubPlannerMappingService>();
        builder.Services.AddScoped<IHubPlannerSyncService, HubPlannerSyncService>();

        // The source-neutral boundary: everything above it resolves sources
        // through ISyncSourceRegistry, never a vendor sync service. Adding a
        // source means one more AddScoped<ISyncSource, ...> line here and
        // nothing else outside its own folder — see
        // Services/Integrations/Abstractions/ISyncSource.
        builder.Services.AddScoped<ISyncSource, ClickUpSyncSource>();
        builder.Services.AddScoped<ISyncSource, HubPlannerSyncSource>();
        builder.Services.AddScoped<ISyncSource, JiraSyncSource>();
        builder.Services.AddScoped<ISyncSource, TempoSyncSource>();
        builder.Services.AddScoped<ISyncSourceRegistry, SyncSourceRegistry>();

        // File import is deliberately not an ISyncSource: it is driven by an
        // upload, not a credential, so it has no "Sync now" button to offer.
        // It shares the Silver tables, run lease and audit trail all the same.
        builder.Services.AddScoped<IDeliveryExportImportService, DeliveryExportImportService>();
        // ...but it does publish, so the data-sources panel reports it.
        builder.Services.AddSingleton(new UploadSource(DeliveryExportImportService.SourceName, "File import"));

        // Scoped, and therefore per sync run: holds which run and which
        // tenant connection is currently writing, so mappers can stamp
        // provenance onto the canonical rows they upsert.
        builder.Services.AddScoped<IIngestionContext, IngestionContext>();

        builder.Services.AddScoped<IProgrammeOverviewQueryService, ProgrammeOverviewQueryService>();
        builder.Services.AddScoped<IReportingQueryService, ReportingQueryService>();
        builder.Services.AddScoped<IRaidService, RaidService>();
        builder.Services.AddScoped<IGovernanceService, GovernanceService>();
        builder.Services.AddScoped<IPortfolioAdminService, PortfolioAdminService>();
        builder.Services.AddScoped<IPortfolioSearchService, PortfolioSearchService>();
        builder.Services.AddScoped<IProgrammeSyncService, ProgrammeSyncService>();
        builder.Services.AddScoped<IMyWorkQueryService, MyWorkQueryService>();
        builder.Services.AddScoped<ISourceConnectionAdminService, SourceConnectionAdminService>();
        builder.Services.AddScoped<IIdentityQueueService, IdentityQueueService>();
        builder.Services.AddScoped<ICodeRepositoryLinkRepository, CodeRepositoryLinkRepository>();
        builder.Services.AddScoped<ICodeRepositoryLinkService, CodeRepositoryLinkService>();
        builder.Services.AddScoped<IProgrammeBudgetService, ProgrammeBudgetService>();
        builder.Services.AddScoped<IReportingSnapshotService, ReportingSnapshotService>();
        builder.Services.AddScoped<IAlertDetectionService, AlertDetectionService>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, ProgrammeOpsMigrationStartupHandler>();
    }
}
