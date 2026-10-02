using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class StaffRoleAssignmentService(
    MemberManager memberManager,
    IStaffRepository staffRepository) : IStaffRoleAssignmentService
{
    public async Task<StaffRoleAssignment?> GetForCurrentMemberAsync()
    {
        var member = await memberManager.GetCurrentMemberAsync();
        if (member is null || !int.TryParse(member.Id, out var memberId))
        {
            return null;
        }

        var staff = await staffRepository.GetByMemberIdAsync(memberId);
        if (staff is null)
        {
            return null;
        }

        var roles = new List<string>();
        foreach (var role in StaffRole.All)
        {
            if (await memberManager.IsMemberAuthorizedAsync(allowGroups: [role]))
            {
                roles.Add(role);
            }
        }

        return new StaffRoleAssignment
        {
            StaffKey = staff.StaffKey,
            MemberId = memberId,
            Roles = roles
        };
    }
}
