using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Single choke point for "what may the current member do". Controllers,
/// views and services ask for a <see cref="Capability"/>, never for a
/// Member Group name and never for a seniority band.
///
/// The seven Is…Async predicates this replaced (IsAdminAsync,
/// IsTeamLeadOrAboveAsync and the rest) were fixed unions of groups. They
/// could not separate reading from writing, so a leave approver inherited
/// delivery write access and a read-only Board could not be expressed at
/// all. They were removed once every call site had migrated — deliberately,
/// rather than left in place, because a second way to ask the same question
/// is how the two definitions drift apart.
///
/// Every capability check also requires an active staff profile, so a member
/// deactivated mid-session loses access on their next request rather than at
/// the end of their cookie's life.
/// </summary>
public interface IStaffAuthorizationService
{
    /// <summary>
    /// Whether the current member holds <paramref name="capability"/>,
    /// resolved through <see cref="RoleCapabilities"/> from the Member Groups
    /// they belong to.
    /// </summary>
    Task<bool> HasAsync(string capability);
}
