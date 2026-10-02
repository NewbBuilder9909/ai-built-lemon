using ProgrammePulse.Models.ViewModels.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Security;

/// <summary>Security's implementation of the MFA operations staff administration needs.</summary>
public sealed class StaffMfaAdministration(IMfaRepository mfaRepository) : IStaffMfaAdministration
{
    public async Task<StaffMfaStatusViewModel> GetStatusAsync(int memberId)
    {
        var mfa = await mfaRepository.GetForMemberAsync(memberId);
        return new StaffMfaStatusViewModel(mfa?.Enabled, mfa?.RecoveryCodes.Count(c => c.UsedAtUtc is null) ?? 0);
    }

    public Task ResetAsync(int memberId) => mfaRepository.ResetAsync(memberId);
}
