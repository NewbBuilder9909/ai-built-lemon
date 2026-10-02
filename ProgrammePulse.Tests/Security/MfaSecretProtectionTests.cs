using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class MfaSecretProtectionTests
{
    // Generated per run: no secret-looking literal in source (Aikido: generic API key).
    private static readonly string TestSeed = ProgrammePulse.Services.Security.TotpAuthenticator.GenerateSecret();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wrong_member_or_missing_key_ring_cannot_decrypt_a_seed(bool wrongMember)
    {
        var provider = new EphemeralDataProtectionProvider();
        var credential = new MemberMfaCredential
        {
            MemberId = wrongMember ? 2 : 1, CredentialVersion = Guid.NewGuid(), Enabled = false,
            ProtectedTotpSecret = new MfaSecretProtection(provider).Protect(1, TestSeed), CreatedAtUtc = DateTime.UtcNow
        };
        IDataProtectionProvider reader = wrongMember ? provider : new EphemeralDataProtectionProvider();
        var service = new MfaVerificationService(new StoredMfaCredentialRepository(credential), new MfaSecretProtection(reader), reader, TimeProvider.System);
        Assert.Null(await service.GetEnrollmentSecretAsync(credential.MemberId, credential.CredentialVersion));
        Assert.Null(await service.ConfirmEnrollmentAsync(credential.MemberId, credential.CredentialVersion, "123456"));
    }

    [Fact]
    public async Task Enabled_secrets_are_never_returned_as_enrollment_material()
    {
        var provider = new EphemeralDataProtectionProvider();
        var credential = new MemberMfaCredential
        {
            MemberId = 1, CredentialVersion = Guid.NewGuid(), Enabled = true,
            ProtectedTotpSecret = new MfaSecretProtection(provider).Protect(1, TestSeed), CreatedAtUtc = DateTime.UtcNow
        };
        var service = new MfaVerificationService(new StoredMfaCredentialRepository(credential), new MfaSecretProtection(provider), provider, TimeProvider.System);
        Assert.Null(await service.GetEnrollmentSecretAsync(1, credential.CredentialVersion));
    }
}
