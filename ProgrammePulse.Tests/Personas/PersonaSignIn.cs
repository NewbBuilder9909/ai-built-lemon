using Microsoft.AspNetCore.Mvc.Testing;
using ProgrammePulse.Tests.Integration;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// Signs a persona in over real HTTP, through the real login action, with
/// the real antiforgery token and the real member cookie — no test
/// authentication handler, no injected ClaimsPrincipal. The point of the
/// persona harness is that the authorization decision is made by the same
/// code path a customer hits.
///
/// Redirects are not auto-followed. Persona assertions need to observe the
/// raw status of a refusal (see the gate document, B2: Forbid() under a
/// cookie scheme may be a 302 rather than a 403), and auto-following would
/// turn a refusal into whatever page it lands on.
/// </summary>
public static class PersonaSignIn
{
    public const string LoginPath = "/staffops/account/login";

    public static HttpClient CreateClient(ProgrammePulseWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            // HTTPS, as production must: cookies marked Secure (the language
            // cookie) are only sent back over it.
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    public const string MfaChallengePath = "/staffops/account/mfa/challenge";

    /// <summary>
    /// Returns a signed-in session, or throws with the reason. Personas whose
    /// group requires MFA additionally clear a real TOTP challenge against
    /// the real <see cref="TotpAuthenticator"/> — the gate's B3. Nothing is
    /// bypassed: the same challenge a customer's Admin sees is answered with
    /// a code computed from the seeded secret.
    /// </summary>
    public static async Task<PersonaSession> SignInAsync(
        ProgrammePulseWebApplicationFactory factory,
        PersonaDefinition persona)
    {
        var client = CreateClient(factory);
        var session = new PersonaSession(client, persona.FullName);

        var token = await session.TryGetAntiForgeryTokenAsync(LoginPath)
            ?? throw new InvalidOperationException($"No antiforgery token on {LoginPath} — the login form did not render.");

        var response = await client.PostAsync(LoginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = persona.Email,
            ["password"] = NorthstarPersonaSeeder.Password,
            ["__RequestVerificationToken"] = token
        }));

        var outcome = PersonaSession.Outcome(response);

        // A 200 here means the login action re-rendered the form with
        // "Invalid email or password" rather than signing anyone in. Left
        // unchecked, every downstream "cannot" assertion would pass simply
        // because nobody was ever authenticated.
        if (outcome is not AccessOutcome.Redirected)
        {
            throw new InvalidOperationException(
                $"Persona '{persona.Key}' did not sign in: POST {LoginPath} returned {(int)response.StatusCode} ({outcome}). "
                + "A 200 means the credentials were rejected; a 429 means the login rate limiter fired.");
        }

        var afterPassword = response.Headers.Location?.ToString() ?? string.Empty;

        // A persona whose group requires MFA must be sent to the challenge,
        // not signed in. Asserting that here means a regression that quietly
        // dropped the MFA requirement would surface as a harness failure
        // rather than as a suite that still passes.
        if (persona.RequiresMfa)
        {
            if (!afterPassword.Contains("/mfa/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Persona '{persona.Key}' is in a group that requires MFA but the password step redirected to '{afterPassword}' "
                    + "instead of an MFA challenge.");
            }

            await ClearTotpChallengeAsync(session, persona);
        }
        else if (afterPassword.Contains("/mfa/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Persona '{persona.Key}' was sent to an MFA challenge but is not declared as requiring MFA.");
        }

        return session;
    }

    private static async Task ClearTotpChallengeAsync(PersonaSession session, PersonaDefinition persona)
    {
        var token = await session.TryGetAntiForgeryTokenAsync(MfaChallengePath)
            ?? throw new InvalidOperationException(
                $"Persona '{persona.Key}' could not load the MFA challenge form — enrollment may not have been confirmed.");

        var verified = await session.Client.PostAsync(MfaChallengePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = CurrentTotpCode(persona.TotpSecret),
            ["__RequestVerificationToken"] = token
        }));

        // A 200 means the challenge view was re-rendered with "that code
        // didn't match" — the persona is not signed in.
        if (PersonaSession.Outcome(verified) is not AccessOutcome.Redirected)
        {
            throw new InvalidOperationException(
                $"Persona '{persona.Key}' failed the MFA challenge: POST {MfaChallengePath} returned {(int)verified.StatusCode}.");
        }
    }

    /// <summary>
    /// Computes the code the real authenticator app would show, via the same
    /// internal HOTP step TotpAuthenticatorTests uses (InternalsVisibleTo).
    /// No production code was added or relaxed to make this testable.
    /// </summary>
    internal static string CurrentTotpCode(string secretBase32)
    {
        var secret = TotpAuthenticator.Base32Decode(secretBase32);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        return TotpAuthenticator.ComputeCode(secret, step);
    }
}
