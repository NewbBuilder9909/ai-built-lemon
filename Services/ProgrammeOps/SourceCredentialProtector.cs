using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ProgrammePulse.Services.ProgrammeOps;

public sealed class SourceCredentialProtector : ISourceCredentialProtector
{
    /// <summary>
    /// A dedicated purpose string, versioned so a future breaking change to
    /// the credential shape can move to .v2 without touching rows already
    /// protected under .v1.
    /// </summary>
    private const string Purpose = "ProgrammePulse.SourceConnection.Credential.v1";

    private readonly IDataProtector protector;

    public SourceCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    {
        protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(SourceCredential credential) =>
        protector.Protect(JsonSerializer.Serialize(credential));

    public SourceCredential? Unprotect(string? protectedCredentialJson)
    {
        if (string.IsNullOrEmpty(protectedCredentialJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SourceCredential>(protector.Unprotect(protectedCredentialJson));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            // CryptographicException: a different/rotated key ring (or genuine
            // tampering). FormatException: the stored value isn't even valid
            // base64url — Unprotect base64url-decodes before it reaches the
            // cryptographic layer, so malformed input surfaces this way, not
            // as a CryptographicException.
            return null;
        }
    }
}
