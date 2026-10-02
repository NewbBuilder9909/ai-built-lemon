using ProgrammePulse.Data.Dtos;
using NPoco;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Scoping;

namespace ProgrammePulse.Services.Security;

/// <summary>Member edits (including password/security/approval changes) invalidate pending logins immediately.</summary>
public sealed class RevokeMfaChallengesOnMemberSaved(IScopeProvider scopeProvider) : INotificationAsyncHandler<MemberSavedNotification>
{
    public async Task HandleAsync(MemberSavedNotification notification, CancellationToken cancellationToken)
    {
        using var scope = scopeProvider.CreateScope();
        foreach (var member in notification.SavedEntities)
            await scope.Database.ExecuteAsync($"DELETE FROM {MemberMfaChallengeDto.TableName} WHERE memberId=@0", member.Id);
        scope.Complete();
    }
}
