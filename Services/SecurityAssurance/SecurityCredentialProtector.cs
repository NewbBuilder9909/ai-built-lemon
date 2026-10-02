using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ProgrammePulse.Services.SecurityAssurance;

/// <summary>A scanning tool's API client credentials.</summary>
public sealed record SecurityToolCredential(string ClientId, string ClientSecret);

/// <summary>
/// Encrypts a tenant's scanning-tool credential for storage. Its own purpose
/// string, and no fallback: a null means the run stops and the admin
/// reconnects, the same rule as the evidence and desk connections. There is
/// no deployment-wide credential to fall back to, by design.
/// </summary>
public interface ISecurityCredentialProtector
{
    string Protect(SecurityToolCredential credential);

    /// <summary>Null when missing, or when the ciphertext can no longer be read (rotated key ring, tampering).</summary>
    SecurityToolCredential? Unprotect(string? protectedCredentialJson);
}

public sealed class SecurityCredentialProtector(IDataProtectionProvider dataProtectionProvider) : ISecurityCredentialProtector
{
    private const string Purpose = "ProgrammePulse.SecurityAssurance.Connection.Credential.v1";

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(SecurityToolCredential credential) =>
        protector.Protect(JsonSerializer.Serialize(credential));

    public SecurityToolCredential? Unprotect(string? protectedCredentialJson)
    {
        if (string.IsNullOrEmpty(protectedCredentialJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SecurityToolCredential>(protector.Unprotect(protectedCredentialJson));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
