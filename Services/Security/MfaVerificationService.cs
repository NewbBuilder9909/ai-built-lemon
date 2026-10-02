using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Security;

/// <summary>Where a member is in MFA enrollment. The only credential fact the sign-in flow needs.</summary>
public enum MfaEnrollmentState
{
    NotStarted,
    Pending,
    Enrolled
}

/// <summary>The only runtime service that decrypts MFA seeds. Enabled seeds never leave this service.</summary>
public sealed class MfaVerificationService(
    IMfaRepository repository, MfaSecretProtection protection, IDataProtectionProvider provider, TimeProvider timeProvider)
{
    public async Task<MfaEnrollmentState> GetEnrollmentStateAsync(int memberId) =>
        await repository.GetForMemberAsync(memberId) switch
        {
            null => MfaEnrollmentState.NotStarted,
            { Enabled: true } => MfaEnrollmentState.Enrolled,
            _ => MfaEnrollmentState.Pending
        };

    /// <summary>Consecutive wrong codes, across every sign-in challenge, before MFA first locks for this member.</summary>
    public const int MaxFailures = 10;

    /// <summary>The first lock. Each further <see cref="MaxFailures"/> wrong codes doubles it, up to <see cref="LongestLock"/>.</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan LongestLock = TimeSpan.FromHours(24);

    /// <summary>
    /// Consecutive wrong codes after which MFA stays locked until an Admin
    /// resets it (NIST SP 800-63B 5.2.2 caps consecutive failures at 100).
    /// </summary>
    public const int MaxConsecutiveFailures = 100;

    /// <summary>The lock a permanent lockout records: SQL Server's datetime ceiling.</summary>
    public static readonly DateTime LockedUntilReset = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// When the member's MFA is locked after repeated wrong codes, the time it
    /// unlocks (<see cref="LockedUntilReset"/> when only an Admin reset will).
    /// The per-challenge budget (MfaChallengeStore) resets with each new
    /// sign-in; this one doesn't, so holding the password isn't enough to keep
    /// guessing (Aikido: excessive authentication attempts).
    /// </summary>
    public Task<DateTime?> GetLockedUntilAsync(int memberId) =>
        repository.GetLockedUntilAsync(memberId, timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    /// The lock the <paramref name="consecutiveFailures"/>th wrong code in a
    /// row sets, or null. A fixed 15-minute lock that reset the count allowed
    /// 960 guesses a day indefinitely, roughly a 1-in-350 daily chance against
    /// a six-digit code with a one-step window; escalating to 24 hours and
    /// stopping at 100 bounds the lifetime total instead.
    /// </summary>
    public static DateTime? LockUntilAfter(int consecutiveFailures, DateTime nowUtc)
    {
        if (consecutiveFailures >= MaxConsecutiveFailures) return LockedUntilReset;
        if (consecutiveFailures < MaxFailures || consecutiveFailures % MaxFailures != 0) return null;
        var doublings = Math.Min(consecutiveFailures / MaxFailures - 1, 16);
        return nowUtc + TimeSpan.FromTicks(Math.Min(LongestLock.Ticks, LockDuration.Ticks << doublings));
    }

    /// <summary>Redeems a one-time recovery code; the remaining count, or null if it was invalid, already used, or MFA is locked.</summary>
    public async Task<int?> RedeemRecoveryCodeAsync(int memberId, string? code)
    {
        if (!await BeginAttemptAsync(memberId)) return null;
        var remaining = await repository.RedeemRecoveryCodeAsync(memberId, stored => MfaRecoveryCodeGenerator.Verify(code, stored), timeProvider.GetUtcNow().UtcDateTime);
        if (remaining is not null) await repository.ClearFailuresAsync(memberId);
        return remaining;
    }

    public async Task<MemberMfaCredential> EnsureEnrollmentAsync(int memberId) =>
        await repository.GetForMemberAsync(memberId)
        ?? await repository.StartEnrollmentAsync(memberId, protection.Protect(memberId, TotpAuthenticator.GenerateSecret()), timeProvider.GetUtcNow().UtcDateTime);

    public async Task<string?> GetEnrollmentSecretAsync(int memberId, Guid credentialVersion)
    {
        var credential = await repository.GetForMemberAsync(memberId);
        return credential is { Enabled: false } && credential.CredentialVersion == credentialVersion ? Decrypt(credential) : null;
    }

    public async Task<bool> VerifyAsync(int memberId, Guid credentialVersion, string? code)
    {
        var credential = await repository.GetForMemberAsync(memberId);
        if (credential is not { Enabled: true } || credential.CredentialVersion != credentialVersion) return false;
        if (!await BeginAttemptAsync(memberId)) return false;
        var step = Match(credential, code);
        var accepted = step is not null && await repository.TryAcceptTotpAsync(credential, step.Value, timeProvider.GetUtcNow().UtcDateTime);
        if (accepted) await repository.ClearFailuresAsync(memberId);
        return accepted;
    }

    public async Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(int memberId, Guid credentialVersion, string? code)
    {
        var credential = await repository.GetForMemberAsync(memberId);
        if (credential is not { Enabled: false } || credential.CredentialVersion != credentialVersion) return null;
        if (!await BeginAttemptAsync(memberId)) return null;
        var step = Match(credential, code);
        var codes = MfaRecoveryCodeGenerator.GenerateCodes();
        var accepted = step is not null && await repository.TryAcceptTotpAsync(credential, step.Value, timeProvider.GetUtcNow().UtcDateTime,
            codes.Select(MfaRecoveryCodeGenerator.Hash).ToList());
        if (accepted) await repository.ClearFailuresAsync(memberId);
        return accepted ? codes : null;
    }

    /// <summary>
    /// Every code is counted as a failure before it is checked, and cleared
    /// only once accepted, so an attempt still in flight has already spent
    /// its place in the budget.
    /// </summary>
    private Task<bool> BeginAttemptAsync(int memberId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return repository.TryBeginAttemptAsync(memberId, now, failures => LockUntilAfter(failures, now));
    }

    private long? Match(MemberMfaCredential credential, string? code)
    {
        var secret = Decrypt(credential);
        return secret is null ? null : TotpAuthenticator.MatchTimeStep(secret, code, timeProvider);
    }

    private string? Decrypt(MemberMfaCredential credential)
    {
        if (!credential.ProtectedTotpSecret.StartsWith(MfaSecretProtection.Prefix, StringComparison.Ordinal)) return null;
        try
        {
            return MfaSecretProtection.ForMember(provider, credential.MemberId)
                .Unprotect(credential.ProtectedTotpSecret[MfaSecretProtection.Prefix.Length..]);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}
