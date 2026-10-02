namespace ProgrammePulse.Models.Staff;

/// <summary>
/// TOTP enrollment for one Umbraco Member (MemberId, not StaffKey — MFA
/// gates the login itself, before a StaffProfile lookup is even meaningful).
/// Enabled flips true only once the member has entered a correct code
/// against the freshly generated secret; a row can exist with Enabled=false
/// mid-enrollment (secret generated, not yet confirmed).
/// </summary>
public sealed record MemberMfaCredential
{
    public required int MemberId { get; init; }

    public required string ProtectedTotpSecret { get; init; }

    public required Guid CredentialVersion { get; init; }

    public long? LastAcceptedTimeStep { get; init; }

    public required bool Enabled { get; init; }

    public DateTime? EnabledAtUtc { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>Empty until enrollment verification atomically enables MFA and stores its recovery hashes.</summary>
    public IReadOnlyList<MfaRecoveryCode> RecoveryCodes { get; init; } = [];
}
