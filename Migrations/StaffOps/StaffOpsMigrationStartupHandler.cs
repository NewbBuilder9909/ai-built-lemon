using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.StaffOps;

/// <summary>
/// Runs the StaffOps migration plan on every startup. Upgrader tracks the plan's
/// last-applied state via IKeyValueService keyed by plan name ("StaffOps"), the
/// same mechanism Umbraco core uses for its own schema upgrades, so this is a
/// safe no-op once the tables already exist.
/// </summary>
public sealed class StaffOpsMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new StaffOpsMigrationPlan());
        var result = await upgrader.ExecuteAsync(migrationPlanExecutor, scopeProvider, keyValueService);
        if (!result.Successful)
            throw new InvalidOperationException("StaffOps migration failed; refusing to run with incomplete MFA security storage.");
    }

    public async Task HandleAsync(IEnumerable<UmbracoApplicationStartingNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await HandleAsync(notification, cancellationToken);
        }
    }
}
