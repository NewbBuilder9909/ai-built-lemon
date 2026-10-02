using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Who is making this request: the signed-in member's id and their staff
/// profile. Scoped, and each lookup is resolved at most once per request.
///
/// Replaces fourteen private controller helpers (GetCurrentStaffAsync,
/// GetCurrentMemberIdAsync, CurrentMemberIdAsync, GetCurrentStaffKeyAsync
/// and the member half of ResolveSelfAsync), which were identical copies of
/// the same member → staff lookup. StaffAuthorizationService and
/// TenantContextAccessor share this instance too, so one request makes one
/// member lookup and one staff lookup however many of them ask.
///
/// The profile is returned whether or not it is active. Access decisions
/// that care about deactivation go through IStaffAuthorizationService,
/// which checks <see cref="StaffProfile.IsActive"/> on every request.
/// </summary>
public interface ICurrentStaff
{
    /// <summary>The Umbraco member id, or null when nobody is signed in.</summary>
    Task<int?> GetMemberIdAsync();

    /// <summary>The member's staff profile, or null when they have none.</summary>
    Task<StaffProfile?> GetProfileAsync();
}

public sealed class CurrentStaff(MemberManager memberManager, IStaffRepository staffRepository) : ICurrentStaff
{
    private Task<int?>? _memberId;
    private Task<StaffProfile?>? _profile;

    public Task<int?> GetMemberIdAsync() => _memberId ??= ResolveMemberIdAsync();

    public Task<StaffProfile?> GetProfileAsync() => _profile ??= ResolveProfileAsync();

    private async Task<int?> ResolveMemberIdAsync()
    {
        var member = await memberManager.GetCurrentMemberAsync();
        return member is not null && int.TryParse(member.Id, out var memberId) ? memberId : null;
    }

    private async Task<StaffProfile?> ResolveProfileAsync() =>
        await GetMemberIdAsync() is { } memberId ? await staffRepository.GetByMemberIdAsync(memberId) : null;
}
