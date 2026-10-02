using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Security;

public interface IMfaRepository
{
    Task<MemberMfaCredential?> GetForMemberAsync(int memberId);

    /// <summary>Creates a not-yet-enabled row with a freshly generated secret if none exists; otherwise returns the existing row unchanged (enrollment doesn't restart once a secret has been issued).</summary>
    Task<MemberMfaCredential> StartEnrollmentAsync(int memberId, string protectedTotpSecret, DateTime nowUtc);

    /// <summary>Atomically consumes a strictly newer verified step for this credential version. Enrollment also enables MFA and stores recovery hashes in this update.</summary>
    Task<bool> TryAcceptTotpAsync(MemberMfaCredential expected, long step, DateTime nowUtc, IReadOnlyList<string>? enrollmentRecoveryHashes = null);

    /// <summary>Replaces recovery hashes without modifying the accepted TOTP step. Enrollment stores its first batch atomically through TryAcceptTotpAsync.</summary>
    Task SetRecoveryCodeHashesAsync(int memberId, IReadOnlyList<string> hashes);

    /// <summary>Marks the first unused code whose stored hash <paramref name="matches"/> accepts as used. Returns the number of codes still unused after, or null if none matched.</summary>
    Task<int?> RedeemRecoveryCodeAsync(int memberId, Func<string, bool> matches, DateTime usedAtUtc);

    /// <summary>Deletes the member's enrollment entirely so they're re-prompted to set up MFA from scratch on next login.</summary>
    Task ResetAsync(int memberId);

    /// <summary>When the member's MFA is locked after repeated wrong codes, the time it unlocks; otherwise null.</summary>
    Task<DateTime?> GetLockedUntilAsync(int memberId, DateTime nowUtc);

    /// <summary>
    /// Spends one attempt before the code is checked: false, and nothing
    /// counted, when the member is locked or has no enrollment. Otherwise
    /// counts the attempt as a consecutive failure until
    /// <see cref="ClearFailuresAsync"/> says it succeeded, and applies the
    /// lock <paramref name="lockUntilAfter"/> returns for the new count. Read
    /// and write happen under one row lock, so parallel guesses queue behind
    /// each other and none can start once the budget is spent. Checking the
    /// lock and counting the failure separately let every request already in
    /// flight through (Aikido: excessive authentication attempts).
    /// </summary>
    Task<bool> TryBeginAttemptAsync(int memberId, DateTime nowUtc, Func<int, DateTime?> lockUntilAfter);

    /// <summary>A correct code clears the failure count and any lock its own attempt set.</summary>
    Task ClearFailuresAsync(int memberId);
}
