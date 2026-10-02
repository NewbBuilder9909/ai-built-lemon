using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Startup;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Security;
using ProgrammePulse.Tests.Security;

namespace ProgrammePulse.Tests.Startup;

public class DataProtectionKeyRingConfigurationTests
{
    // Generated per run: no secret-looking literal in source (Aikido: generic API key).
    private static readonly string TestSeed = ProgrammePulse.Services.Security.TotpAuthenticator.GenerateSecret();

    [Fact]
    public void Production_uses_azure_blob_key_ring_when_blob_storage_is_configured()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["AzureStorage:ContainerName"] = "programmepulse-data-protection"
        }).Build();

        DataProtectionKeyRingConfiguration.Configure(services, configuration, "Production");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider));
    }

    [Fact]
    public async Task Production_protected_data_survives_a_new_service_provider_using_the_same_key_ring()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "programme-pulse-keyring-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=ProgrammePulseTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var certificatePath = Path.Combine(testDirectory, "key-protector.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-password"));
            var keyRingDirectory = Path.Combine(testDirectory, "keys");
            Directory.CreateDirectory(keyRingDirectory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingDirectory"] = keyRingDirectory,
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = "test-password"
            }).Build();

            using var first = BuildProvider(configuration);
            var protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test-purpose").Protect("credential-secret");
            var mfa = new MemberMfaCredential
            {
                MemberId = 42, CredentialVersion = Guid.NewGuid(), Enabled = false, CreatedAtUtc = DateTime.UtcNow,
                ProtectedTotpSecret = new MfaSecretProtection(first.GetRequiredService<IDataProtectionProvider>()).Protect(42, TestSeed)
            };

            using var second = BuildProvider(configuration);
            var plainText = second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test-purpose").Unprotect(protectedValue);

            Assert.Equal("credential-secret", plainText);
            Assert.Equal(TestSeed, await ReadEnrollmentAsync(second, mfa));
            var keyFile = Assert.Single(Directory.GetFiles(keyRingDirectory, "key-*.xml"));
            Assert.Contains("encryptedSecret", File.ReadAllText(keyFile), StringComparison.OrdinalIgnoreCase);

            using var replacementRsa = RSA.Create(2048);
            var replacementRequest = new CertificateRequest("CN=ProgrammePulseReplacement", replacementRsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var replacementCertificate = replacementRequest.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var replacementPath = Path.Combine(testDirectory, "replacement.pfx");
            File.WriteAllBytes(replacementPath, replacementCertificate.Export(X509ContentType.Pfx, "new-password"));
            var rotatedConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingDirectory"] = keyRingDirectory,
                ["DataProtection:CertificatePath"] = replacementPath,
                ["DataProtection:CertificatePassword"] = "new-password",
                ["DataProtection:PreviousCertificatePath"] = certificatePath,
                ["DataProtection:PreviousCertificatePassword"] = "test-password"
            }).Build();
            using var rotated = BuildProvider(rotatedConfiguration);
            Assert.Equal("credential-secret", rotated.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test-purpose").Unprotect(protectedValue));
            Assert.Equal(TestSeed, await ReadEnrollmentAsync(rotated, mfa));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        DataProtectionKeyRingConfiguration.Configure(services, configuration, "Production");
        return services.BuildServiceProvider();
    }

    private static Task<string?> ReadEnrollmentAsync(IServiceProvider services, MemberMfaCredential credential)
    {
        var provider = services.GetRequiredService<IDataProtectionProvider>();
        return new MfaVerificationService(new StoredMfaCredentialRepository(credential), new MfaSecretProtection(provider), provider, TimeProvider.System)
            .GetEnrollmentSecretAsync(credential.MemberId, credential.CredentialVersion);
    }
}
