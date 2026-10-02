using Microsoft.AspNetCore.Http;

namespace ProgrammePulse.Services.Security;

/// <summary>
/// Wraps the short-lived "MfaPending" cookie authentication scheme
/// (registered in Program.cs) that holds an Admin's identity between a
/// correct password and a correct TOTP code — a real ASP.NET Core cookie
/// scheme backed by an expiring, single-use SQL challenge and a separate
/// browser-binding cookie. Account stamp and enrollment version are checked
/// on every use. Deliberately a separate scheme from the
/// real Umbraco Member sign-in cookie: nothing is granted access by holding
/// only this cookie.
/// </summary>
public interface IMfaChallengeStore
{
    Task StartAsync(HttpContext httpContext, string memberId, string securityStamp, string? returnUrl);

    Task<(string MemberId, string? ReturnUrl, Guid CredentialVersion)?> GetAsync(HttpContext httpContext);

    Task ClearAsync(HttpContext httpContext);

    /// <summary>Revalidates the account and atomically consumes the server-side challenge before sign-in.</summary>
    Task<bool> TryConsumeAsync(HttpContext httpContext, string memberId);

    /// <summary>Records a failed MFA verification attempt and invalidates the challenge if the maximum attempt limit is exceeded.</summary>
    Task<bool> RecordFailedAttemptAsync(HttpContext httpContext);
}
