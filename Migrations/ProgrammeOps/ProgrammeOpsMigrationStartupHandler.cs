using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.ProgrammeOps;

/// <summary>
/// Runs the ProgrammeOps migration plan on every startup. Same mechanism as
/// Migrations/StaffOps/StaffOpsMigrationStartupHandler — tracked by plan name
/// ("ProgrammeOps") via IKeyValueService, so it's a safe no-op once the
/// tables already exist and runs independently of the StaffOps plan.
/// </summary>
public sealed class ProgrammeOpsMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new ProgrammeOpsMigrationPlan());
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
