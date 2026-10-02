using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.ExecutiveReview;

public sealed record ReviewActor(Guid TenantId, int MemberId);

public interface IReviewAccess
{
    Task<ReviewActor> RequireAsync(string capability);
}

public sealed class ReviewAccess(
    IStaffAuthorizationService authorization, ITenantContext tenant,
    MemberManager members, IStaffRepository staff) : IReviewAccess
{
    public async Task<ReviewActor> RequireAsync(string capability)
    {
        if (!await authorization.HasAsync(capability)) throw new UnauthorizedAccessException();
        await tenant.EnsureResolvedAsync();
        if (!tenant.IsResolved || tenant.CurrentTenantId is not Guid tenantId)
            throw new UnauthorizedAccessException();
        var member = await members.GetCurrentMemberAsync();
        if (member is null || !int.TryParse(member.Id, out var memberId)) throw new UnauthorizedAccessException();
        var profile = await staff.GetByMemberIdAsync(memberId);
        if (profile is not { IsActive: true } || profile.TenantId != tenantId) throw new UnauthorizedAccessException();
        return new ReviewActor(tenantId, memberId);
    }
}
