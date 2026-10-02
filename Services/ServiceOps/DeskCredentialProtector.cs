using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Models.ServiceOps;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// Encrypts a tenant's desk credential for storage in
/// ServiceOps_DeskConnection.protectedCredentialJson.
///
/// Its own purpose string, separate from both ProgrammeOps' and the
/// evidence area's, for the reason set out on IEvidenceCredentialProtector:
/// ProgrammeOps' protector is documented to return null so the sync can
/// fall back to a deployment-wide token. There is no fallback here. A
/// null means the run stops and the admin reconnects.
/// </summary>
public interface IDeskCredentialProtector
{
    string Protect(DeskCredential credential);

    /// <summary>
    /// Null in, null out. Also null when the ciphertext can no longer be
    /// unprotected — a rotated key ring, or tampering. Callers treat that
    /// as "reconnect required", never as "use something else".
    /// </summary>
    DeskCredential? Unprotect(string? protectedCredentialJson);
}

public sealed class DeskCredentialProtector : IDeskCredentialProtector
{
    private const string Purpose = "ProgrammePulse.ServiceOps.DeskConnection.Credential.v1";

    private readonly IDataProtector protector;

    public DeskCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    {
        protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(DeskCredential credential) =>
        protector.Protect(JsonSerializer.Serialize(credential));

    public DeskCredential? Unprotect(string? protectedCredentialJson)
    {
        if (string.IsNullOrEmpty(protectedCredentialJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeskCredential>(protector.Unprotect(protectedCredentialJson));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
