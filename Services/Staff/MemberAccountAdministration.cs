using Microsoft.AspNetCore.Identity;
using ProgrammePulse.Models.Staff;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;

namespace ProgrammePulse.Services.Staff;

/// <summary>A member's sign-in account, as staff administration needs to see it.</summary>
public sealed record MemberAccountState(bool IsApproved, DateTimeOffset? LockedOutUntil, IReadOnlyList<string> Roles)
{
    public bool IsLockedOut => LockedOutUntil is { } until && until > DateTimeOffset.UtcNow;
}

/// <summary>
/// The Umbraco member (login) side of staff administration: approval,
/// lockout, roles, password reset tokens and deletion. Behind an interface
/// so the rules in StaffAdminService run against a fake; MemberManager is a
/// concrete Identity class the tests can't stand up.
/// </summary>
public interface IMemberAccountAdministration
{
    Task<MemberAccountState?> GetStateAsync(int memberId);

    Task SetApprovedAsync(int memberId, bool approved);

    /// <summary>Clears a lockout from repeated wrong passwords.</summary>
    Task UnlockAsync(int memberId);

    /// <summary>Replaces the member's tenant roles (<see cref="StaffRole.All"/>); other groups are left alone.</summary>
    Task<IReadOnlyList<string>> SetRolesAsync(int memberId, IReadOnlyCollection<string> roles);

    /// <summary>The ids of every member in the Admin group, across tenants; the caller intersects with its own roster.</summary>
    Task<IReadOnlySet<int>> GetAdminMemberIdsAsync();

    /// <summary>A one-time token that lets the holder set a new password; null when the member doesn't exist.</summary>
    Task<string?> CreatePasswordResetTokenAsync(int memberId);

    Task<IReadOnlyList<string>> ResetPasswordAsync(int memberId, string token, string newPassword);

    /// <summary>Deletes the login. The staff profile and its history are the caller's to handle first.</summary>
    Task DeleteAsync(int memberId);
}

public sealed class MemberAccountAdministration(MemberManager memberManager, IMemberService memberService) : IMemberAccountAdministration
{
    public async Task<MemberAccountState?> GetStateAsync(int memberId)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString());
        if (member is null)
        {
            return null;
        }

        var roles = await memberManager.GetRolesAsync(member);
        var lockout = await memberManager.GetLockoutEndDateAsync(member);
        return new MemberAccountState(member.IsApproved, lockout, roles.Where(StaffRole.All.Contains).Order(StringComparer.Ordinal).ToList());
    }

    public async Task SetApprovedAsync(int memberId, bool approved)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString()) ?? throw new InvalidOperationException($"Member {memberId} not found.");
        member.IsApproved = approved;
        Throw(await memberManager.UpdateAsync(member));

        // A new stamp ends any session the person already has open.
        Throw(await memberManager.UpdateSecurityStampAsync(member));
    }

    public async Task UnlockAsync(int memberId)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString()) ?? throw new InvalidOperationException($"Member {memberId} not found.");
        Throw(await memberManager.SetLockoutEndDateAsync(member, null));
        Throw(await memberManager.ResetAccessFailedCountAsync(member));
    }

    public async Task<IReadOnlyList<string>> SetRolesAsync(int memberId, IReadOnlyCollection<string> roles)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString()) ?? throw new InvalidOperationException($"Member {memberId} not found.");
        var current = (await memberManager.GetRolesAsync(member)).Where(StaffRole.All.Contains).ToList();
        var wanted = roles.Where(StaffRole.All.Contains).Distinct(StringComparer.Ordinal).ToList();

        var remove = current.Except(wanted, StringComparer.Ordinal).ToList();
        if (remove.Count > 0)
        {
            Throw(await memberManager.RemoveFromRolesAsync(member, remove));
        }

        var add = wanted.Except(current, StringComparer.Ordinal).ToList();
        if (add.Count > 0)
        {
            Throw(await memberManager.AddToRolesAsync(member, add));
        }

        // Role claims ride in the cookie; a new stamp makes the next request re-read them.
        Throw(await memberManager.UpdateSecurityStampAsync(member));
        return wanted.Order(StringComparer.Ordinal).ToList();
    }

    public Task<IReadOnlySet<int>> GetAdminMemberIdsAsync() =>
        Task.FromResult<IReadOnlySet<int>>(memberService.GetMembersByGroup(StaffRole.Admin).Select(m => m.Id).ToHashSet());

    public async Task<string?> CreatePasswordResetTokenAsync(int memberId)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString());
        return member is null ? null : await memberManager.GeneratePasswordResetTokenAsync(member);
    }

    public async Task<IReadOnlyList<string>> ResetPasswordAsync(int memberId, string token, string newPassword)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString());
        if (member is null)
        {
            return ["This reset link isn't valid."];
        }

        var result = await memberManager.ResetPasswordAsync(member, token, newPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Select(e => e.Code == "InvalidToken" ? "This reset link has expired or has already been used." : e.Description).ToList();
        }

        // A reset is also the way back in after too many wrong passwords.
        await memberManager.SetLockoutEndDateAsync(member, null);
        await memberManager.ResetAccessFailedCountAsync(member);
        return [];
    }

    public async Task DeleteAsync(int memberId)
    {
        var member = await memberManager.FindByIdAsync(memberId.ToString());
        if (member is not null)
        {
            Throw(await memberManager.DeleteAsync(member));
        }
    }

    private static void Throw(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
