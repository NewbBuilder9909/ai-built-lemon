using System.Net;
using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Personas;

/// <summary>
/// One signed-in persona's HTTP session against the real application, plus
/// the two mechanics a persona assertion cannot be trusted without — see
/// docs/persona-harness-build-gate.md B2 and B4.
///
/// B4 is the important one: every POST in this application carries
/// [ValidateAntiForgeryToken]. A "cannot" test that posts without a token
/// gets a 400 from the antiforgery filter, which looks exactly like a
/// successful authorization denial while proving nothing. So this type
/// always fetches and sends a real token, and <see cref="Outcome"/>
/// classifies 400 as <see cref="AccessOutcome.Malformed"/> — never as a
/// denial — so such a test fails loudly instead of passing quietly.
/// </summary>
public sealed partial class PersonaSession(HttpClient client, string personaName)
{
    public string PersonaName { get; } = personaName;

    public HttpClient Client { get; } = client;

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiForgeryTokenPattern();

    public async Task<HttpResponseMessage> GetAsync(string path) => await Client.GetAsync(path);

    /// <summary>
    /// Fetches <paramref name="formPath"/>, lifts its antiforgery token, and
    /// posts <paramref name="fields"/> to <paramref name="postPath"/>. If the
    /// form page itself is refused there is no token to send, which is
    /// reported as <see cref="AccessOutcome.NoForm"/> rather than silently
    /// posting without one.
    /// </summary>
    public async Task<HttpResponseMessage?> PostWithTokenAsync(
        string formPath,
        string postPath,
        IDictionary<string, string> fields)
    {
        var token = await TryGetAntiForgeryTokenAsync(formPath);
        if (token is null)
        {
            return null;
        }

        var payload = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await Client.PostAsync(postPath, new FormUrlEncodedContent(payload));
    }

    public async Task<string?> TryGetAntiForgeryTokenAsync(string formPath)
    {
        var response = await Client.GetAsync(formPath);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenPattern().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Where this application sends a refused member. Measured in Phase 0,
    /// not assumed: <c>Forbid()</c> here never produces a 403 — Umbraco's
    /// member cookie scheme turns it into a 302 to this path. See
    /// docs/persona-harness-build-gate.md, B2.
    /// </summary>
    public const string AccessDeniedPath = "/Account/AccessDenied";

    public const string LoginPath = "/staffops/account/login";

    /// <summary>
    /// Classifies a response into the vocabulary persona expectations are
    /// written in. Only <see cref="AccessOutcome.Denied"/> may satisfy a
    /// "cannot" expectation.
    ///
    /// The redirect handling is the subtle part. A denial and a successful
    /// POST are *both* 302 here, so status alone cannot tell them apart —
    /// the same class of trap as antiforgery (B4), one level up. A redirect
    /// counts as a denial only when it points at the access-denied handler
    /// or back at the login page; any other redirect is a completed action.
    /// </summary>
    public static AccessOutcome Outcome(HttpResponseMessage response)
    {
        if (IsRedirect(response.StatusCode))
        {
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            return location.Contains(AccessDeniedPath, StringComparison.OrdinalIgnoreCase)
                || location.Contains(LoginPath, StringComparison.OrdinalIgnoreCase)
                ? AccessOutcome.Denied
                : AccessOutcome.Redirected;
        }

        // A 5xx is the application failing, not deciding. Kept as its own
        // outcome because it must never read as either an allowance or a
        // denial: an intermittent 500 once looked like an authorization
        // regression in this suite, and "Other" gave nothing to diagnose it
        // with. Deliberately not retried - masking instability would be
        // worse than a loud failure.
        if ((int)response.StatusCode >= 500)
        {
            return AccessOutcome.ServerError;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.OK => AccessOutcome.Allowed,
            HttpStatusCode.Forbidden => AccessOutcome.Denied,
            HttpStatusCode.Unauthorized => AccessOutcome.Denied,
            HttpStatusCode.BadRequest => AccessOutcome.Malformed,
            HttpStatusCode.TooManyRequests => AccessOutcome.RateLimited,
            _ => AccessOutcome.Other
        };
    }

    // Redirect is 302 and RedirectMethod is 303 — HttpStatusCode.Found and
    // SeeOther are aliases of those same values, not extra cases.
    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect;
}

public enum AccessOutcome
{
    Allowed,
    Denied,
    Redirected,
    /// <summary>A 400 — almost always a missing antiforgery token. Never a valid denial.</summary>
    Malformed,
    RateLimited,
    /// <summary>The form page needed for an antiforgery token was itself refused.</summary>
    NoForm,
    /// <summary>A 5xx - the application failed rather than making an access decision.</summary>
    ServerError,
    Other
}
