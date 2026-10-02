using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace ProgrammePulse.Services.Security;

/// <summary>Write-only protection boundary used by enrollment and the legacy-data migration.</summary>
public sealed class MfaSecretProtection(IDataProtectionProvider provider)
{
    internal const string Prefix = "mfa:v1:";

    internal static IDataProtector ForMember(IDataProtectionProvider provider, int memberId) =>
        provider.CreateProtector("ProgrammePulse.Mfa.TotpSecret.v1", memberId.ToString(CultureInfo.InvariantCulture));

    public string Protect(int memberId, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return Prefix + ForMember(provider, memberId).Protect(secret);
    }
}
