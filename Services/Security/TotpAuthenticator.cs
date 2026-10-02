using System.Security.Cryptography;

namespace ProgrammePulse.Services.Security;

/// <summary>
/// Hand-rolled RFC 6238 (TOTP) / RFC 4226 (HOTP) — no new NuGet dependency,
/// matching the rest of this codebase's preference for small self-contained
/// pieces over adding a package for one narrow job (see
/// ContractDocumentStorageService's magic-byte check for the same instinct).
/// 30-second step, 6 digits, SHA-1 (the Google/Microsoft/Authy-compatible
/// defaults every authenticator app assumes when no algorithm is specified
/// in the otpauth:// URI).
/// </summary>
public static class TotpAuthenticator
{
    private const int SecretByteLength = 20;
    private const int TimeStepSeconds = 30;
    private const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(SecretByteLength));

    public static string BuildOtpAuthUri(string secretBase32, string accountLabel, string issuer) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountLabel)}" +
        $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={TimeStepSeconds}";

    /// <summary>Allows one 30-second step of drift either side of "now" to tolerate clock skew.</summary>
    public static bool ValidateCode(string secretBase32, string? code, TimeProvider timeProvider, int allowedDriftSteps = 1)
        => MatchTimeStep(secretBase32, code, timeProvider, allowedDriftSteps) is not null;

    public static long? MatchTimeStep(string secretBase32, string? code, TimeProvider timeProvider, int allowedDriftSteps = 1)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }

        byte[] secret;
        try
        {
            secret = Base32Decode(secretBase32);
        }
        catch (FormatException)
        {
            return null;
        }

        var currentStep = timeProvider.GetUtcNow().ToUnixTimeSeconds() / TimeStepSeconds;
        for (var drift = allowedDriftSteps; drift >= -allowedDriftSteps; drift--)
        {
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(ComputeCode(secret, currentStep + drift)), System.Text.Encoding.ASCII.GetBytes(code)))
            {
                return currentStep + drift;
            }
        }

        return null;
    }

    internal static string ComputeCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var hash = HMACSHA1.HashData(secret, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    internal static string Base32Encode(byte[] data)
    {
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                output.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return output.ToString();
    }

    internal static byte[] Base32Decode(string base32)
    {
        var cleaned = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>((cleaned.Length * 5) / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var c in cleaned)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException($"'{c}' is not a valid Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return [.. output];
    }
}
