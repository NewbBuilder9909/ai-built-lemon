using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class MfaRecoveryCodeGeneratorTests
{
    [Fact]
    public void GenerateCodes_produces_the_requested_count_of_distinct_formatted_codes()
    {
        var codes = MfaRecoveryCodeGenerator.GenerateCodes(8);

        Assert.Equal(8, codes.Count);
        Assert.Equal(8, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", c));
    }

    [Fact]
    public void GenerateCodes_never_uses_ambiguous_characters()
    {
        var codes = MfaRecoveryCodeGenerator.GenerateCodes(20);

        Assert.All(codes, c => Assert.DoesNotContain(c, "0O1I"));
    }

    [Fact]
    public void A_stored_hash_verifies_its_code_and_is_salted()
    {
        var code = MfaRecoveryCodeGenerator.GenerateCodes(1)[0];
        var stored = MfaRecoveryCodeGenerator.Hash(code);

        Assert.True(MfaRecoveryCodeGenerator.Verify(code, stored));
        Assert.StartsWith("pbkdf2-sha256$", stored);
        // Salted: the same code never stores the same value, so a copied table can't be matched against a precomputed list.
        Assert.NotEqual(stored, MfaRecoveryCodeGenerator.Hash(code));
        Assert.DoesNotContain(code.Replace("-", ""), stored);
    }

    [Fact]
    public void Verify_ignores_casing_hyphenation_and_surrounding_whitespace()
    {
        var code = MfaRecoveryCodeGenerator.GenerateCodes(1)[0];
        var messy = "  " + code.ToLowerInvariant().Replace("-", " ") + "  ";

        Assert.True(MfaRecoveryCodeGenerator.Verify(messy, MfaRecoveryCodeGenerator.Hash(code)));
    }

    [Fact]
    public void Verify_rejects_a_different_code_an_empty_code_and_a_malformed_hash()
    {
        var codes = MfaRecoveryCodeGenerator.GenerateCodes(2);
        var stored = MfaRecoveryCodeGenerator.Hash(codes[0]);

        Assert.False(MfaRecoveryCodeGenerator.Verify(codes[1], stored));
        Assert.False(MfaRecoveryCodeGenerator.Verify(null, stored));
        Assert.False(MfaRecoveryCodeGenerator.Verify(codes[0], "pbkdf2-sha256$100000$not-base64$also-not"));
    }

    [Fact]
    public void A_legacy_unsalted_hash_no_longer_verifies_even_for_its_own_code()
    {
        // Accepting it kept every pre-27-September code brute-forceable from a
        // copied database until it was used (Aikido: MFA bypass).
        var code = MfaRecoveryCodeGenerator.GenerateCodes(1)[0];
        var legacy = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(MfaRecoveryCodeGenerator.Normalize(code))));

        Assert.False(MfaRecoveryCodeGenerator.Verify(code, legacy));
    }
}
