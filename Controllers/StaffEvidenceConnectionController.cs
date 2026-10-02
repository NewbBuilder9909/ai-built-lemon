using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using ProgrammePulse.Services.Integrations.GitHub;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Tenant-admin management of engineering evidence sources: connect a
/// GitHub App installation on selected repositories, run a read-only
/// sync, and approve which external accounts belong to which staff member.
///
/// Three security properties this flow is responsible for:
///
/// 1. **The callback is bound to the session that started it.** A
///    protected, short-lived, single-use cookie carries the tenant, the
///    member and a nonce; the callback is refused unless all three still
///    match. Same mechanism as StaffOAuthConnectionController, which is
///    the established pattern in this codebase. An HTTP concern, so it
///    stays in this controller.
/// 2. **The API host comes from an allow-list, never from the request.**
///    EvidenceHostPolicy validates it, and operator configuration is the
///    only way to add an Enterprise host.
/// 3. **The granted account is verified against the provider**, not taken
///    from the form (IGitHubConnectionService). An admin who types one
///    organisation and authorises another is told, rather than silently
///    connected to the wrong one.
///
/// The provider-neutral work (the sources page, disconnect, the identity
/// mapping queue) lives in IEvidenceSourceAdminService.
///
/// Plan-gated: a tenant whose plan lacks GitHubEvidence gets a 403 before
/// any action runs. That is a commercial control on top of — never instead
/// of — the [RequireCapability] on each action.
/// </summary>
[Route("staffops/skills/evidence")]
[RequireFeature(ProductFeature.GitHubEvidence)]
[RequireModule(ProductModules.Skills)]
public sealed class StaffEvidenceConnectionController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    IEvidenceSourceAdminService sources,
    IIntegrationRunStatusQueryService runStatus,
    IGitHubConnectionService gitHubConnections,
    GitHubEvidenceIngestionService ingestion,
    IOptions<SkillsEvidenceOptions> options,
    IDataProtectionProvider dataProtection,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : Controller
{
    private const string StateCookie = "pp-evidence-oauth";
    private const string ConnectionsPath = "/staffops/skills/evidence";
    private const string ActorsPath = "/staffops/skills/evidence/actors";

    private readonly IDataProtector stateProtector = dataProtection.CreateProtector("ProgrammePulse.SkillsEvidence.OAuth.State.v1");

    [HttpGet("")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await sources.BuildConnectionsPageAsync(tenantId, IsAppConfigured()) with
        {
            RunStates = await runStatus.GetStatesAsync(tenantId,
            [
                (GitHubEvidenceIngestionService.SourceName, "GitHub evidence"),
                (AzureDevOpsEvidenceIngestionService.SourceName, "Azure DevOps evidence"),
            ]),
        };

        ViewData["Title"] = "Engineering evidence sources";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Evidence/Connections.cshtml", model);
    }

    /// <summary>
    /// Starts the GitHub App installation flow. The repository selection
    /// itself happens on GitHub — this records which account the admin
    /// intends, so the callback can check they authorised that one.
    /// </summary>
    [HttpPost("github/start")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> StartGitHub(string organisation, string? apiBaseUrl)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        if (!IsAppConfigured())
        {
            return BadRequest("The GitHub App is not configured on this deployment.");
        }

        var host = EvidenceHostPolicy.Canonicalize(
            string.IsNullOrWhiteSpace(apiBaseUrl) ? EvidenceHostPolicy.GitHubDotComApi : apiBaseUrl,
            options.Value.AllowedEnterpriseHosts);
        if (host is null)
        {
            return BadRequest("That API host is not permitted. Only github.com, or an Enterprise host this deployment allows.");
        }

        var account = organisation?.Trim();
        if (string.IsNullOrWhiteSpace(account) || !EvidenceHostPolicy.IsValidRepositoryKey($"{account}/placeholder"))
        {
            return BadRequest("Enter the GitHub organisation or user the app will be installed on.");
        }

        var state = new EvidenceOAuthState(
            tenantId, staff.MemberId, account!, host,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            timeProvider.GetUtcNow().AddMinutes(10));

        Response.Cookies.Append(StateCookie, stateProtector.Protect(JsonSerializer.Serialize(state)), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
            IsEssential = true
        });

        var url = "https://github.com/apps/" + Uri.EscapeDataString(options.Value.GitHubAppSlug!)
            + "/installations/new?" + QueryString.Create(new Dictionary<string, string?>
            {
                ["state"] = state.Nonce
            }).Value?.TrimStart('?');

        return Redirect(url);
    }

    [HttpGet("github/callback")]
    public async Task<IActionResult> GitHubCallback(string? code, string? state, string? installation_id, string? error, CancellationToken cancellationToken)
    {
        var pending = await ValidateCallbackAsync(state);
        if (pending is null)
        {
            return Forbid();
        }

        Response.Cookies.Delete(StateCookie);

        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
        {
            return Back("GitHub authorisation was cancelled.");
        }

        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        var result = await gitHubConnections.CompleteInstallationAsync(
            tenantId, staff.StaffKey, staff.MemberId, pending.ApiBaseUrl, pending.SourceAccountId, code, installation_id, cancellationToken);
        return Back(result.Message ?? string.Empty);
    }

    [HttpPost("{connectionKey:guid}/sync")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sync")]
    [RequireCapability(Capability.TriggerSync)]
    public async Task<IActionResult> Sync(Guid connectionKey, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        try
        {
            var result = await ingestion.RunAsync(connectionKey, tenantId, staff.MemberId, cancellationToken);
            return Back(result.Summary);
        }
        catch (SkillAssertionValidationException ex)
        {
            return Back(ex.Message);
        }
    }

    [HttpPost("{connectionKey:guid}/disconnect")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Disconnect(Guid connectionKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return Back(await sources.DisconnectAsync(tenantId, connectionKey, staff.MemberId));
    }

    // ---- Identity mapping ----

    [HttpGet("actors")]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Actors([CurrentTenant] Guid tenantId, string? message = null, int page = 1)
    {
        var model = await sources.BuildActorsPageAsync(tenantId, new PageRequest(page));

        ViewData["Title"] = "Unidentified source accounts";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Evidence/Actors.cshtml", model);
    }

    [HttpPost("actors/{unmappedActorKey:guid}/approve")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> ApproveActor(Guid unmappedActorKey, Guid staffKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (approver, tenantId) = context.Value;
        return ToActorsResponse(await sources.ApproveActorAsync(tenantId, unmappedActorKey, staffKey, approver.StaffKey, approver.MemberId));
    }

    [HttpPost("actors/links/{linkKey:guid}/revoke")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> RevokeActorLink(Guid linkKey, Guid connectionKey, string externalActorId)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (approver, tenantId) = context.Value;
        return ToActorsResponse(await sources.RevokeActorLinkAsync(tenantId, linkKey, connectionKey, externalActorId, approver.MemberId));
    }

    // ---- helpers ----

    private bool IsAppConfigured() =>
        !string.IsNullOrWhiteSpace(options.Value.GitHubAppSlug)
        && !string.IsNullOrWhiteSpace(options.Value.GitHubClientId)
        && !string.IsNullOrWhiteSpace(options.Value.GitHubClientSecret);

    private async Task<EvidenceOAuthState?> ValidateCallbackAsync(string? nonce)
    {
        if (!await staffAuthorizationService.HasAsync(Capability.ManageIntegrations)
            || string.IsNullOrEmpty(nonce)
            || !Request.Cookies.TryGetValue(StateCookie, out var cookie))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<EvidenceOAuthState>(stateProtector.Unprotect(cookie));
            var tenantId = await tenantContext.ResolveTenantIdAsync();
            var memberId = await currentStaff.GetMemberIdAsync();

            return state is not null
                && state.ExpiresAtUtc > timeProvider.GetUtcNow()
                && state.TenantId == tenantId
                && state.MemberId == memberId
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state.Nonce), Encoding.UTF8.GetBytes(nonce))
                ? state
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private IActionResult ToActorsResponse(CommandResult result) =>
        result.Status == CommandStatus.NotFound ? NotFound() : Back(result.Message ?? string.Empty, EvidenceBackTarget.Actors);

    // A fixed set of local targets and LocalRedirect: no message or path from
    // the request can ever turn this into an off-site redirect (PR #14).
    private IActionResult Back(string message, EvidenceBackTarget target = EvidenceBackTarget.Connections) =>
        LocalRedirect(QueryHelpers.AddQueryString(target == EvidenceBackTarget.Actors ? ActorsPath : ConnectionsPath, "message", message));

    private enum EvidenceBackTarget
    {
        Connections,
        Actors,
    }

    private sealed record EvidenceOAuthState(
        Guid TenantId, int MemberId, string SourceAccountId, string ApiBaseUrl, string Nonce, DateTimeOffset ExpiresAtUtc);
}
