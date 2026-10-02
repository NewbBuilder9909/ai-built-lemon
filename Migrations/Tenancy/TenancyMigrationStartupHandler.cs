using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Migrations.Tenancy;

/// <summary>
/// Runs the Tenancy migration plan on every startup. Same mechanism as
/// Migrations/BrandingOps/BrandingOpsMigrationStartupHandler — tracked by
/// plan name ("Tenancy") via IKeyValueService, independent of every other
/// plan, and must have no dependency on those plans' run order (see
/// Tenant.DefaultTenantKey's doc comment).
/// </summary>
public sealed class TenancyMigrationStartupHandler(
    IMigrationPlanExecutor migrationPlanExecutor,
    IScopeProvider scopeProvider,
    IKeyValueService keyValueService) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var upgrader = new Upgrader(new TenancyMigrationPlan());
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
