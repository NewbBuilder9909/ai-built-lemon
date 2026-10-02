using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Staff;

public sealed class StaffAuthorizationService(MemberManager memberManager, ICurrentStaff currentStaff) : IStaffAuthorizationService
{
    public async Task<bool> HasAsync(string capability) =>
        await HasActiveProfileAsync() && RoleCapabilities.Has(await CurrentRolesAsync(), capability);

    // Cached for the lifetime of this scoped service (the profile through the
    // scoped ICurrentStaff, shared with tenant resolution): a single request
    // asks several times (the layout alone asks for a handful), while a new
    // request still observes a deactivation or a group change immediately.
    private Task<bool>? _activeProfileTask;
    private Task<IReadOnlyList<string>>? _rolesTask;

    /// <summary>
    /// Group membership alone is not enough. A member with no staff profile,
    /// or a deactivated one, holds no capability — checked on every request
    /// rather than only at login, so deactivation takes effect immediately.
    /// Covered by PersonaBoundaryTests; removing it fails that test.
    /// </summary>
    private Task<bool> HasActiveProfileAsync() => _activeProfileTask ??= ResolveActiveProfileAsync();

    private async Task<bool> ResolveActiveProfileAsync() => (await currentStaff.GetProfileAsync())?.IsActive == true;

    private Task<IReadOnlyList<string>> CurrentRolesAsync() => _rolesTask ??= ResolveRolesAsync();

    private async Task<IReadOnlyList<string>> ResolveRolesAsync()
    {
        var member = await memberManager.GetCurrentMemberAsync();
        return member is null ? [] : [.. await memberManager.GetRolesAsync(member)];
    }
}
