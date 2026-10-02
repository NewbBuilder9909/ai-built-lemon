using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

/// <summary>Read-only encrypted fixture; verification writes are covered against SQL.</summary>
internal sealed class StoredMfaCredentialRepository(MemberMfaCredential credential) : IMfaRepository
{
    public Task<MemberMfaCredential?> GetForMemberAsync(int memberId) => Task.FromResult<MemberMfaCredential?>(credential.MemberId == memberId ? credential : null);
    public Task<MemberMfaCredential> StartEnrollmentAsync(int memberId, string protectedTotpSecret, DateTime nowUtc) => throw new NotSupportedException();
    public Task<bool> TryAcceptTotpAsync(MemberMfaCredential expected, long step, DateTime nowUtc, IReadOnlyList<string>? enrollmentRecoveryHashes = null) => throw new NotSupportedException();
    public Task SetRecoveryCodeHashesAsync(int memberId, IReadOnlyList<string> hashes) => throw new NotSupportedException();
    public Task<int?> RedeemRecoveryCodeAsync(int memberId, Func<string, bool> matches, DateTime usedAtUtc) => throw new NotSupportedException();
    public Task ResetAsync(int memberId) => throw new NotSupportedException();
    public Task<DateTime?> GetLockedUntilAsync(int memberId, DateTime nowUtc) => Task.FromResult<DateTime?>(null);
    public Task<bool> TryBeginAttemptAsync(int memberId, DateTime nowUtc, Func<int, DateTime?> lockUntilAfter) => Task.FromResult(credential.MemberId == memberId);
    public Task ClearFailuresAsync(int memberId) => Task.CompletedTask;
}
