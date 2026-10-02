using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Idempotently creates the StaffRole Member Groups (the five tenant roles plus
/// Platform Admin) on startup so the rest
/// of the app (StaffAuthorizationService, member assignment via the backoffice)
/// can rely on them always existing.
/// </summary>
public sealed class StaffGroupSeeder(IMemberGroupService memberGroupService)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        foreach (var roleName in StaffRole.Seeded)
        {
            var existing = await memberGroupService.GetByNameAsync(roleName);
            if (existing is null)
            {
                await memberGroupService.CreateAsync(new MemberGroup { Name = roleName });
            }
        }
    }

    public async Task HandleAsync(IEnumerable<UmbracoApplicationStartedNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await HandleAsync(notification, cancellationToken);
        }
    }
}
