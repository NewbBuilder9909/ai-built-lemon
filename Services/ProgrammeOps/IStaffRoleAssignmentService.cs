using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Projects the currently signed-in member's Umbraco Member Group membership
/// into a StaffRoleAssignment. Scoped to "current member" only —
/// MemberManager.IsMemberAuthorizedAsync checks the signed-in member from
/// HttpContext, it has no "check this other member's groups" overload, so
/// looking up an arbitrary staff member's roles would need IMemberService
/// group queries instead. That's a documented Phase 3 extension point (see
/// docs/programme-ops.md), not implemented here to avoid shipping a method
/// that looks like it takes any staff member but actually always answers for
/// whoever is logged in.
/// </summary>
public interface IStaffRoleAssignmentService
{
    Task<StaffRoleAssignment?> GetForCurrentMemberAsync();
}
