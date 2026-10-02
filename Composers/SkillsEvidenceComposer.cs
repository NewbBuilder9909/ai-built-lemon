using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Migrations.SkillsEvidence;
using ProgrammePulse.Services.Integrations;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using ProgrammePulse.Services.Integrations.GitHub;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.SkillsEvidence;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace ProgrammePulse.Composers;

/// <summary>
/// Registers the Skills and Evidence domain: the SkillsEvidence_* schema
/// migration, the one repository covering both aggregates, the assertion
/// lifecycle service, the aggregate coverage query, its own audit log, and
/// its participation in GDPR export/erasure.
///
/// A composer of its own rather than a few lines added to
/// StaffOperationsComposer — the rule in CLAUDE.md, and the reason this
/// area's migration plan can fail on startup without taking StaffOps with
/// it. TimeProvider is registered once, by StaffOperationsComposer;
/// deliberately not re-registered here.
/// </summary>
public sealed class SkillsEvidenceComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<ISkillsEvidenceRepository, SkillsEvidenceRepository>();
        builder.Services.AddScoped<ISkillsEvidenceAuditLogRepository, SkillsEvidenceAuditLogRepository>();
        builder.Services.AddScoped<ISkillAssertionService, SkillAssertionService>();
        builder.Services.AddScoped<ISkillCoverageQueryService, SkillCoverageQueryService>();
        builder.Services.AddScoped<IContributionReviewRepository, ContributionReviewRepository>();
        builder.Services.AddScoped<ContributionReviewService>();
        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, ContributionReviewDataParticipant>();

        // --- Slice 2: engineering evidence ---
        builder.Services.Configure<SkillsEvidenceOptions>(
            builder.Config.GetSection(SkillsEvidenceOptions.SectionName));

        builder.Services.AddScoped<IEngineeringEvidenceRepository, EngineeringEvidenceRepository>();
        builder.Services.AddSingleton<IEvidenceCredentialProtector, EvidenceCredentialProtector>();

        // Scoped, so one sync run gets one resolver whose link cache is
        // loaded once — and so its unmapped/ambiguous/bot counters describe
        // that run rather than accumulating across the process.
        builder.Services.AddScoped<IEvidenceActorResolver, EvidenceActorResolver>();
        builder.Services.AddScoped<GitHubEvidenceIngestionService>();
        builder.Services.AddScoped<IEvidencePortfolioQueryService, EvidencePortfolioQueryService>();

        // --- Slice 4: continuity plan, coverage actions and the
        // data-processing decision that gates evidence collection. ---
        builder.Services.AddScoped<IContinuityRepository, ContinuityRepository>();
        builder.Services.AddScoped<IContinuityService, ContinuityService>();
        builder.Services.AddScoped<IKeyPersonCoverageQueryService, KeyPersonCoverageQueryService>();
        builder.Services.AddScoped<IEnablementReadinessService, EnablementReadinessService>();
        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, ContinuityDataParticipant>();

        // --- Slice 5: the suggestion queue. Proposals only; nothing
        // here creates a fact without a person accepting it. ---
        builder.Services.AddScoped<ISuggestionRepository, SuggestionRepository>();
        builder.Services.AddScoped<ISuggestionService, SuggestionService>();

        // No BaseAddress: the host comes from the connection's validated
        // ApiBaseUrl on every request, so a pooled default can never send a
        // tenant's token somewhere the allow-list did not sanction.
        builder.Services.AddHttpClient<IGitHubEvidenceClient, GitHubEvidenceClient>(
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        // --- Azure DevOps Services: the second forge, writing the same
        // provider-neutral evidence rows. Options bind the same section
        // so evidence Bronze has one retention setting. No redirects, no
        // cookies: a refused token must surface as a refusal, not be
        // followed to a sign-in page and read as an empty result. ---
        builder.Services.Configure<AzureDevOpsEvidenceOptions>(
            builder.Config.GetSection(SkillsEvidenceOptions.SectionName));
        builder.Services.AddScoped<AzureDevOpsEvidenceIngestionService>();
        builder.Services.AddScoped<IAzureDevOpsConnectionService, AzureDevOpsConnectionService>();
        builder.Services.AddScoped<IGitHubConnectionService, GitHubConnectionService>();
        builder.Services.AddScoped<IEvidenceSourceAdminService, EvidenceSourceAdminService>();
        builder.Services.AddScoped<IContinuityPageService, ContinuityPageService>();
        builder.Services.AddScoped<ISkillsPageService, SkillsPageService>();
        builder.Services.AddHttpClient<IAzureDevOpsEvidenceClient, AzureDevOpsEvidenceClient>(
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(OutboundEndpointPolicy.CreateHandler)
            .AddHttpMessageHandler<TransientHttpRetryHandler>();

        // Additive: GdprService takes every registered IStaffDataParticipant,
        // so a subject access request picks this up without the Staff
        // domain knowing this area exists.
        builder.Services.AddScoped<Services.Staff.IStaffDataParticipant, SkillsEvidenceDataParticipant>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, SkillsEvidenceMigrationStartupHandler>();
    }
}
