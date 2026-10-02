using ProgrammePulse.Models.ViewModels.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// What staff administration needs from MFA: a person's enrollment status,
/// and the Admin break-glass reset. Defined here in Staff and implemented in
/// Services/Security (StaffMfaAdministration). Security already depends on
/// Staff, because a sign-in challenge checks that the staff profile is still
/// active. If Staff called the MFA repository directly, the two areas would
/// depend on each other, which FeatureAreaDependencyTests refuses.
/// </summary>
public interface IStaffMfaAdministration
{
    Task<StaffMfaStatusViewModel> GetStatusAsync(int memberId);

    Task ResetAsync(int memberId);
}
