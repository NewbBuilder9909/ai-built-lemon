using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Core.Services;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Answers "which members hold only oversight roles?" for capacity views —
/// see <see cref="StaffRole.OversightOnly"/> for the rule and why.
///
/// Needs arbitrary members' groups, which <c>MemberManager</c> cannot answer
/// (it only knows the signed-in member), so this follows the
/// <c>IMemberService.GetMembersByGroup</c> precedent in StaffAccountController.
/// Member Groups are global, not tenant-scoped; callers intersect the result
/// with their own tenant's staff, so it never widens what a page can see.
/// </summary>
public interface IDeliveryRoleDirectory
{
    /// <summary>Member ids holding at least one oversight role and no other role.</summary>
    Task<IReadOnlySet<int>> GetOversightOnlyMemberIdsAsync();
}

public sealed class DeliveryRoleDirectory(IMemberService memberService) : IDeliveryRoleDirectory
{
    public Task<IReadOnlySet<int>> GetOversightOnlyMemberIdsAsync()
    {
        HashSet<int> MembersOf(IEnumerable<string> groups) =>
            groups.SelectMany(memberService.GetMembersByGroup).Select(m => m.Id).ToHashSet();

        var oversightOnly = MembersOf(StaffRole.OversightOnly);
        oversightOnly.ExceptWith(MembersOf(StaffRole.Seeded.Except(StaffRole.OversightOnly)));
        return Task.FromResult<IReadOnlySet<int>>(oversightOnly);
    }
}
