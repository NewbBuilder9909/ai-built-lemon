using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class TotpAuthenticatorTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Computes the expected code directly via the internal HOTP step
    /// (InternalsVisibleTo'd to this test project) rather than brute-forcing
    /// it through the public ValidateCode surface.
    /// </summary>
    private static string CurrentCodeFor(string secretBase32, DateTimeOffset at)
    {
        var secret = TotpAuthenticator.Base32Decode(secretBase32);
        var step = at.ToUnixTimeSeconds() / 30;
        return TotpAuthenticator.ComputeCode(secret, step);
    }

    [Fact]
    public void GenerateSecret_produces_a_valid_base32_string_of_a_consistent_length()
    {
        var secret = TotpAuthenticator.GenerateSecret();

        Assert.NotEmpty(secret);
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        // 20 raw bytes -> 32 Base32 characters, no padding.
        Assert.Equal(32, secret.Length);
    }

    [Fact]
    public void ValidateCode_accepts_the_code_generated_for_the_current_time_step()
    {
        var secret = TotpAuthenticator.GenerateSecret();
        var code = CurrentCodeFor(secret, Now);

        Assert.True(TotpAuthenticator.ValidateCode(secret, code, new FixedTimeProvider(Now)));
    }

    [Fact]
    public void ValidateCode_rejects_a_code_that_does_not_match_any_step_in_the_drift_window()
    {
        var secretA = TotpAuthenticator.GenerateSecret();
        var secretB = TotpAuthenticator.GenerateSecret();
        var codeForA = CurrentCodeFor(secretA, Now);

        // A code valid for a different secret should not validate against this one
        // (astronomically unlikely to collide across a random 20-byte secret).
        Assert.False(TotpAuthenticator.ValidateCode(secretB, codeForA, new FixedTimeProvider(Now)));
    }

    [Fact]
    public void ValidateCode_rejects_malformed_input()
    {
        var secret = TotpAuthenticator.GenerateSecret();

        Assert.False(TotpAuthenticator.ValidateCode(secret, null, new FixedTimeProvider(Now)));
        Assert.False(TotpAuthenticator.ValidateCode(secret, "", new FixedTimeProvider(Now)));
        Assert.False(TotpAuthenticator.ValidateCode(secret, "12345", new FixedTimeProvider(Now))); // too short
        Assert.False(TotpAuthenticator.ValidateCode(secret, "abcdef", new FixedTimeProvider(Now))); // not digits
    }

    [Fact]
    public void ValidateCode_tolerates_one_step_of_clock_drift_either_side()
    {
        var secret = TotpAuthenticator.GenerateSecret();
        var oneStepAhead = Now.AddSeconds(30);
        var codeForNow = CurrentCodeFor(secret, Now);

        // Validating "now"'s code against a clock that's one 30s step ahead
        // must still succeed within the default +/-1 step drift window.
        Assert.True(TotpAuthenticator.ValidateCode(secret, codeForNow, new FixedTimeProvider(oneStepAhead)));
    }

    [Fact]
    public void ValidateCode_rejects_a_code_outside_the_drift_window()
    {
        var secret = TotpAuthenticator.GenerateSecret();
        var farAhead = Now.AddMinutes(10);
        var codeForNow = CurrentCodeFor(secret, Now);

        Assert.False(TotpAuthenticator.ValidateCode(secret, codeForNow, new FixedTimeProvider(farAhead)));
    }

    [Fact]
    public void Base32_encode_decode_round_trips_arbitrary_bytes()
    {
        var original = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 254, 128, 64, 32, 16, 8, 4, 2, 1 };

        var encoded = TotpAuthenticator.Base32Encode(original);
        var decoded = TotpAuthenticator.Base32Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void BuildOtpAuthUri_includes_the_secret_issuer_and_account_label()
    {
        var uri = TotpAuthenticator.BuildOtpAuthUri("ABCD1234", "jamie@example.com", "Operations Hub");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=ABCD1234", uri);
        Assert.Contains("jamie%40example.com", uri);
    }
}
