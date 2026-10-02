using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Security;

/// <summary>
/// Whether a member may sign in at all: they need an active staff profile.
/// Checked after the password and again at the final sign-in (after MFA), so
/// someone deactivated between the two steps is still refused. The member is
/// not signed in yet at either point, so this looks them up by id rather
/// than through the request's ICurrentStaff.
/// </summary>
public interface ISignInEligibility
{
    Task<bool> IsActiveStaffMemberAsync(int memberId);
}

public sealed class SignInEligibility(IStaffRepository staffRepository) : ISignInEligibility
{
    public async Task<bool> IsActiveStaffMemberAsync(int memberId) =>
        (await staffRepository.GetByMemberIdAsync(memberId))?.IsActive == true;
}
