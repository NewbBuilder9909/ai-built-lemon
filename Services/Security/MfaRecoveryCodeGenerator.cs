using System.Security.Cryptography;

namespace ProgrammePulse.Services.Security;

/// <summary>
/// One-time recovery codes for the TOTP MFA flow (see TotpAuthenticator) —
/// the break-glass path for an Admin who has enrolled but lost access to
/// their authenticator app, with no other Admin available to reset them via
/// StaffAdminController's MFA reset action. Codes are shown to the member
/// exactly once, at enrollment confirmation; only a salted PBKDF2 hash is ever
/// persisted (MemberMfaDto.RecoveryCodesJson), matching TotpAuthenticator's
/// stance of never storing anything that lets a DB read alone impersonate
/// the member — a stolen hash can't be turned back into a usable code.
/// </summary>
public static class MfaRecoveryCodeGenerator
{
    private const int CodeCount = 8;
    private const int GroupLength = 5;

    // Excludes 0/O and 1/I so a handwritten copy can't be misread.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Generates a fresh batch of plaintext codes, formatted "ABCDE-FGHJK", to show to the member once.</summary>
    public static IReadOnlyList<string> GenerateCodes(int count = CodeCount)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add(GenerateCode());
        }

        return codes;
    }

    private static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(GroupLength * 2);
        var chars = new char[GroupLength * 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return $"{new string(chars, 0, GroupLength)}-{new string(chars, GroupLength, GroupLength)}";
    }

    /// <summary>Strips formatting so a hyphen/case/whitespace difference between entry and generation never causes a false mismatch.</summary>
    public static string Normalize(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private const string Scheme = "pbkdf2-sha256";

    /// <summary>
    /// PBKDF2 work factor. A code carries 50 bits of entropy, so this is about
    /// defeating fast offline guessing from a copied database, not about a weak
    /// secret; checking all eight of a member's codes stays well under a second.
    /// </summary>
    private const int Iterations = 100_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// A salted, slow hash for storage: "pbkdf2-sha256$iterations$salt$hash".
    /// Each call uses a fresh salt, so the same code never stores the same
    /// value twice; compare with <see cref="Verify"/>, never by equality.
    /// Before 27 September 2026 codes were stored as an unsalted SHA-256, which a
    /// copied database could brute-force offline (Aikido: MFA bypass).
    /// </summary>
    public static string Hash(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Normalize(code), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Whether <paramref name="code"/> matches a stored hash, in constant time.
    /// Only the salted form verifies. The legacy unsalted SHA-256 form no longer
    /// does: while it was accepted, every code issued before 27 September stayed
    /// brute-forceable from a copied database until it happened to be used
    /// (Aikido: MFA bypass). A member holding only old codes signs in with their
    /// authenticator, or asks another Admin to reset MFA, and gets a new batch.
    /// </summary>
    public static bool Verify(string? code, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Normalize(code ?? string.Empty), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
