using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// Real ASP.NET Core Data Protection against a temporary, disposable on-disk
/// key ring (never the application's own) — proves the actual encrypt/decrypt
/// round-trip and the graceful fallback-to-null on a value that can no longer
/// be unprotected, which ClickUpSyncServiceTests/HubPlannerSyncServiceTests
/// fake around with a plain JSON pass-through instead of real encryption.
/// </summary>
public sealed class SourceCredentialProtectorTests : IDisposable
{
    private readonly string keyRingDirectory = Path.Combine(Path.GetTempPath(), "ProgrammePulseTests", Guid.NewGuid().ToString("N"));

    public SourceCredentialProtectorTests()
    {
        Directory.CreateDirectory(keyRingDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(keyRingDirectory, recursive: true);
    }

    private SourceCredentialProtector BuildSut() =>
        new(DataProtectionProvider.Create(new DirectoryInfo(keyRingDirectory)));

    [Fact]
    public void A_protected_credential_round_trips_through_unprotect()
    {
        var sut = BuildSut();
        var credential = new SourceCredential("secret-token", "workspace-42");

        var protectedValue = sut.Protect(credential);

        Assert.Equal(credential, sut.Unprotect(protectedValue));
        Assert.DoesNotContain("secret-token", protectedValue);
    }

    [Fact]
    public void Null_in_is_null_out()
    {
        Assert.Null(BuildSut().Unprotect(null));
    }

    [Fact]
    public void A_malformed_value_returns_null_instead_of_throwing()
    {
        Assert.Null(BuildSut().Unprotect("not-a-real-protected-value"));
    }

    [Fact]
    public void A_value_protected_under_a_different_key_ring_returns_null_instead_of_throwing()
    {
        var otherKeyRingDirectory = Path.Combine(Path.GetTempPath(), "ProgrammePulseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherKeyRingDirectory);
        try
        {
            var otherProtector = new SourceCredentialProtector(DataProtectionProvider.Create(new DirectoryInfo(otherKeyRingDirectory)));
            var protectedElsewhere = otherProtector.Protect(new SourceCredential("token"));

            Assert.Null(BuildSut().Unprotect(protectedElsewhere));
        }
        finally
        {
            Directory.Delete(otherKeyRingDirectory, recursive: true);
        }
    }
}
