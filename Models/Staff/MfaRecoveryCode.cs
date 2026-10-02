namespace ProgrammePulse.Models.Staff;

/// <summary>
/// One hashed, single-use recovery code (see MfaRecoveryCodeGenerator) — the
/// plaintext is shown to the member exactly once at enrollment and never
/// persisted. UsedAtUtc is set the first time it's redeemed on the MFA
/// challenge screen; a used code is never valid again.
/// </summary>
public sealed record MfaRecoveryCode(string Hash, DateTime? UsedAtUtc);
