using ProgrammePulse.Models.Staff;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Integrations.OAuth;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Tenant-admin initiated OAuth connections. A protected, short-lived,
/// one-use browser cookie binds each callback to its initiating member and
/// tenant. The OAuth mechanics (state cookie, nonce, redirect validation)
/// are HTTP concerns and stay here. Storing the resulting credential goes
/// through ISourceConnectionAdminService, which also audits it, the same as
/// a ClickUp or Hub Planner credential.
/// </summary>
[Route("staffops/programme/oauth")]
public sealed class StaffOAuthConnectionController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService authorization,
    ITenantContext tenantContext,
    ISourceConnectionAdminService connectionAdmin,
    ConnectorOAuthClient oauth,
    IOptions<ConnectorOAuthOptions> options,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider) : Controller
{
    private const string StateCookie = "ops-connector-oauth";
    private readonly IDataProtector stateProtector = dataProtection.CreateProtector("ProgrammePulse.ConnectorOAuth.State.v1");

    [HttpGet("")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = new OAuthConnectionsViewModel(
            await connectionAdmin.DescribeOAuthConnectionAsync(tenantId, "Jira"),
            await connectionAdmin.DescribeOAuthConnectionAsync(tenantId, "Tempo"));
        ViewData["Title"] = "Jira and Tempo connections";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Programme/OAuthConnections.cshtml", model);
    }

    [HttpPost("jira/start")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> StartJira([CurrentTenant] Guid tenantId, Guid cloudId, string? projectIds)
    {
        var memberId = await currentStaff.GetMemberIdAsync();
        if (memberId is null) return Forbid();
        if (!Configured(options.Value.JiraClientId, options.Value.JiraClientSecret)
            || !ValidRedirect(options.Value.JiraRedirectUri, "/staffops/programme/oauth/jira/callback"))
            return BadRequest("Jira OAuth is not configured on this deployment.");
        // No projects is a deliberate choice, not a mistake: an identity-only
        // connection gives Tempo its site and people without importing issues.
        // It is stored as an empty list; a missing list still means "reconnect".
        var ids = string.Empty;
        if (cloudId == Guid.Empty || (!string.IsNullOrWhiteSpace(projectIds) && !TryProjectIds(projectIds, out ids)))
            return BadRequest("Enter a Jira cloud ID, and either comma-separated numeric project IDs or none for an identity-only connection.");

        var state = NewState("Jira", tenantId, memberId.Value, cloudId.ToString("D"), ids, null, null);
        SetStateCookie(state);
        var url = "https://auth.atlassian.com/authorize?" + QueryString.Create(new Dictionary<string, string?>
        {
            ["audience"] = "api.atlassian.com", ["client_id"] = options.Value.JiraClientId,
            ["scope"] = "read:jira-work offline_access", ["redirect_uri"] = options.Value.JiraRedirectUri,
            ["state"] = state.Nonce, ["response_type"] = "code", ["prompt"] = "consent"
        }).Value?.TrimStart('?');
        return Redirect(url);
    }

    [HttpGet("jira/callback")]
    public async Task<IActionResult> JiraCallback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        var pending = await ValidateCallbackAsync("Jira", state);
        if (pending is null) return Forbid();
        ClearStateCookie();
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code)) return Back("Jira authorization was cancelled.");

        try
        {
            var tokens = await oauth.ExchangeJiraAsync(code, cancellationToken);
            var cloudId = Guid.Parse(pending.Scope);
            var siteUrl = await oauth.JiraSiteUrlAsync(cloudId, tokens.AccessToken, cancellationToken);
            if (siteUrl is null)
                return Back("The Jira authorization does not grant access to the selected site.");
            await connectionAdmin.SaveCredentialAsync(pending.TenantId, "Jira", new SourceCredential(
                tokens.AccessToken, cloudId.ToString("D"), tokens.RefreshToken, tokens.ExpiresAtUtc,
                SiteUrl: siteUrl, ProjectIds: pending.ProjectIds), pending.MemberId);
            return Back(string.IsNullOrEmpty(pending.ProjectIds)
                ? "Jira connected for identity only: no issues will be imported. You can now connect Tempo for this site."
                : "Jira OAuth credential stored. Run a read-only sync to verify project access and reporting data.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return Back("Jira authorization could not be completed. Check the app configuration and site access.");
        }
    }

    [HttpPost("tempo/start")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> StartTempo([CurrentTenant] Guid tenantId, string jiraSiteUrl, string clientId, string clientSecret)
    {
        var memberId = await currentStaff.GetMemberIdAsync();
        if (memberId is null) return Forbid();
        if (!Configured(clientId, clientSecret)
            || !ValidRedirect(options.Value.TempoRedirectUri, "/staffops/programme/oauth/tempo/callback")
            || !TryJiraSite(jiraSiteUrl, out var site))
            return BadRequest("Enter a Jira Cloud site and Tempo OAuth application credentials.");
        var jiraCredential = await connectionAdmin.GetCredentialAsync(tenantId, "Jira");
        if (jiraCredential?.SiteUrl is null || !string.Equals(jiraCredential.SiteUrl, site, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Connect the matching Jira site before connecting Tempo.");

        var state = NewState("Tempo", tenantId, memberId.Value, site!, null, clientId, clientSecret);
        SetStateCookie(state);
        var url = "https://api.tempo.io/oauth/authorize/redirect?" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId, ["redirect_uri"] = options.Value.TempoRedirectUri,
            ["jira_url"] = site, ["state"] = state.Nonce
        }).Value?.TrimStart('?');
        return Redirect(url);
    }

    [HttpGet("tempo/callback")]
    public async Task<IActionResult> TempoCallback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        var pending = await ValidateCallbackAsync("Tempo", state);
        if (pending is null) return Forbid();
        ClearStateCookie();
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code)) return Back("Tempo authorization was cancelled.");

        try
        {
            var tokens = await oauth.ExchangeTempoAsync(code, pending.ClientId!, pending.ClientSecret!, cancellationToken);
            await connectionAdmin.SaveCredentialAsync(pending.TenantId, "Tempo", new SourceCredential(
                tokens.AccessToken, RefreshToken: tokens.RefreshToken, ExpiresAtUtc: tokens.ExpiresAtUtc,
                SiteUrl: pending.Scope, ClientId: pending.ClientId, ClientSecret: pending.ClientSecret), pending.MemberId);
            return Back("Tempo OAuth credential stored. Run a read-only sync to verify worklog access and reporting data.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            return Back("Tempo authorization could not be completed. Check the OAuth application and site access.");
        }
    }

    private static bool Configured(params string?[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));

    private static bool ValidRedirect(string? value, string path) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool TryProjectIds(string text, out string ids)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var valid = parts.Length is > 0 and <= 50 && parts.All(p => long.TryParse(p, out var id) && id > 0);
        ids = valid ? string.Join(',', parts.Distinct()) : string.Empty;
        return valid;
    }

    private static bool TryJiraSite(string value, out string? site)
    {
        site = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/") return false;
        site = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private OAuthState NewState(string provider, Guid tenantId, int memberId, string scope, string? projectIds, string? clientId, string? clientSecret) =>
        new(provider, tenantId, memberId, scope, projectIds, clientId, clientSecret,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), timeProvider.GetUtcNow().AddMinutes(10));

    private void SetStateCookie(OAuthState state) => Response.Cookies.Append(StateCookie,
        stateProtector.Protect(JsonSerializer.Serialize(state)),
        new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });

    private void ClearStateCookie() => Response.Cookies.Delete(StateCookie);

    private async Task<OAuthState?> ValidateCallbackAsync(string provider, string? nonce)
    {
        if (!await authorization.HasAsync(Capability.ManageIntegrations) || string.IsNullOrEmpty(nonce)
            || !Request.Cookies.TryGetValue(StateCookie, out var cookie)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<OAuthState>(stateProtector.Unprotect(cookie));
            var tenantId = await tenantContext.ResolveTenantIdAsync();
            var memberId = await currentStaff.GetMemberIdAsync();
            return state is not null && state.Provider == provider && state.ExpiresAtUtc > timeProvider.GetUtcNow()
                && state.TenantId == tenantId && state.MemberId == memberId
                && CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(state.Nonce), System.Text.Encoding.UTF8.GetBytes(nonce))
                ? state : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private IActionResult Back(string message) => Redirect("/staffops/programme/oauth?message=" + Uri.EscapeDataString(message));

    private sealed record OAuthState(string Provider, Guid TenantId, int MemberId, string Scope, string? ProjectIds,
        string? ClientId, string? ClientSecret, string Nonce, DateTimeOffset ExpiresAtUtc);
}
