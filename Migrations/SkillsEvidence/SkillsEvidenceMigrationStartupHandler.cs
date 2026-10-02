using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Runs the SkillsEvidence migration plan on every startup — tracked by
/// plan name ("SkillsEvidence") via IKeyValueService, a safe no-op once the
/// tables already exist and independent of every other plan.
/// </summary>
public sealed class SkillsEvidenceMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new SkillsEvidenceMigrationPlan());
        await upgrader.ExecuteAsync(migrationPlanExecutor, scopeProvider, keyValueService);
    }

    public async Task HandleAsync(IEnumerable<UmbracoApplicationStartingNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await HandleAsync(notification, cancellationToken);
        }
    }
}
