using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Staff;

/// <summary>In-memory member accounts: approval, lockout, roles and deletion, keyed by member id.</summary>
public sealed class FakeMemberAccountAdministration : IMemberAccountAdministration
{
    public readonly Dictionary<int, (bool Approved, DateTimeOffset? LockedUntil, List<string> Roles)> Accounts = [];
    public readonly List<int> Deleted = [];

    public FakeMemberAccountAdministration With(int memberId, params string[] roles)
    {
        Accounts[memberId] = (true, null, roles.ToList());
        return this;
    }

    public Task<MemberAccountState?> GetStateAsync(int memberId) =>
        Task.FromResult(Accounts.TryGetValue(memberId, out var a)
            ? new MemberAccountState(a.Approved, a.LockedUntil, a.Roles.Order(StringComparer.Ordinal).ToList())
            : null);

    public Task SetApprovedAsync(int memberId, bool approved)
    {
        var a = Accounts[memberId];
        Accounts[memberId] = (approved, a.LockedUntil, a.Roles);
        return Task.CompletedTask;
    }

    public Task UnlockAsync(int memberId)
    {
        var a = Accounts[memberId];
        Accounts[memberId] = (a.Approved, null, a.Roles);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SetRolesAsync(int memberId, IReadOnlyCollection<string> roles)
    {
        var a = Accounts[memberId];
        Accounts[memberId] = (a.Approved, a.LockedUntil, roles.ToList());
        return Task.FromResult<IReadOnlyList<string>>(roles.Order(StringComparer.Ordinal).ToList());
    }

    public Task<IReadOnlySet<int>> GetAdminMemberIdsAsync() =>
        Task.FromResult<IReadOnlySet<int>>(Accounts.Where(a => a.Value.Roles.Contains(Models.Staff.StaffRole.Admin)).Select(a => a.Key).ToHashSet());

    public Task<string?> CreatePasswordResetTokenAsync(int memberId) =>
        Task.FromResult(Accounts.ContainsKey(memberId) ? $"token-{memberId}" : null);

    public Task<IReadOnlyList<string>> ResetPasswordAsync(int memberId, string token, string newPassword) =>
        Task.FromResult<IReadOnlyList<string>>(token == $"token-{memberId}" ? [] : ["This reset link has expired or has already been used."]);

    public Task DeleteAsync(int memberId)
    {
        Accounts.Remove(memberId);
        Deleted.Add(memberId);
        return Task.CompletedTask;
    }
}
