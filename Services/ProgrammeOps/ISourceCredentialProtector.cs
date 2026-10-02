namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Encrypts/decrypts a tenant's own source credential for storage in
/// ProgrammeOps_SourceConnection.ProtectedCredentialJson. Wraps ASP.NET
/// Core's Data Protection API (no new NuGet dependency) rather than a
/// hand-rolled cipher — the same "use what the platform already solved"
/// choice as TransientHttpRetryHandler over Polly and TotpAuthenticator over
/// a new MFA library. Unlike StaffOps_MemberMfa.totpSecret (stored in
/// plaintext today), this is the one place in the codebase a
/// recoverable-at-rest secret is actually encrypted.
/// </summary>
public interface ISourceCredentialProtector
{
    string Protect(SourceCredential credential);

    /// <summary>
    /// Null in, null out (no credential configured for this connection).
    /// Also returns null if the stored value can no longer be unprotected
    /// (e.g. the Data Protection key ring changed) — a sync falls back to
    /// the deployment-wide credential rather than failing outright.
    /// </summary>
    SourceCredential? Unprotect(string? protectedCredentialJson);
}
