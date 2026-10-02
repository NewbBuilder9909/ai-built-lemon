using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Encrypts a tenant's evidence-source credential for storage in
/// SkillsEvidence_Connection.protectedCredentialJson.
///
/// Separate from ProgrammeOps' ISourceCredentialProtector, with its own
/// purpose string, for a reason that is not ceremony: that one is
/// documented to return null when a credential cannot be read so the sync
/// can *fall back to the deployment-wide token*. There is no fallback
/// here, and sharing a protector would invite sharing that behaviour.
/// A null from this protector means the run stops.
/// </summary>
public interface IEvidenceCredentialProtector
{
    string Protect(EvidenceCredential credential);

    /// <summary>
    /// Null in, null out. Also null if the ciphertext can no longer be
    /// unprotected — a rotated key ring, or tampering. The caller must
    /// treat that as "reconnect required", never as "use something else".
    /// </summary>
    EvidenceCredential? Unprotect(string? protectedCredentialJson);
}

public sealed class EvidenceCredentialProtector : IEvidenceCredentialProtector
{
    private const string Purpose = "ProgrammePulse.SkillsEvidence.Connection.Credential.v1";

    private readonly IDataProtector protector;

    public EvidenceCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    {
        protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(EvidenceCredential credential) =>
        protector.Protect(JsonSerializer.Serialize(credential));

    public EvidenceCredential? Unprotect(string? protectedCredentialJson)
    {
        if (string.IsNullOrEmpty(protectedCredentialJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EvidenceCredential>(protector.Unprotect(protectedCredentialJson));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
