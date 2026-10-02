using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Lets a tenant's own Admin set or clear its own ClickUp/Hub Planner
/// credential, so its sync reaches its own account instead of the
/// deployment-wide ClickUp:ApiToken/HubPlanner:ApiKey fallback (see
/// Models/Programme/SourceConnection and Services/ProgrammeOps/
/// ISourceCredentialProtector). Admin-only and tenant-scoped like the rest
/// of Programme Ops. The credential is write-only from here: once saved, it
/// is never echoed back to the browser, only whether one is configured.
///
/// Deliberately two source-specific actions rather than one generic form
/// routed through ISyncSource — a credential's shape genuinely differs per
/// source (ClickUp needs a workspace id alongside its token, Hub Planner
/// needs only a key), and extending the source-neutral boundary to describe
/// credential shapes is out of scope for this phase (see docs/programme-ops.md).
/// Source names are passed as the same literal strings ClickUpSyncService/
/// HubPlannerSyncService use as their own SourceName constants (an
/// established invariant — see ISyncSource.Name's doc comment) rather than
/// referencing those vendor types directly.
/// </summary>
[Route("staffops/programme/connections")]
public sealed class StaffSourceConnectionController(
    ICurrentStaff currentStaff,
    ISourceConnectionAdminService connectionAdmin,
    ILogger<StaffSourceConnectionController> logger) : Controller
{
    public const string AuditEntityType = SourceConnectionAdminService.AuditEntityType;
    private const string ClickUpSource = "ClickUp";
    private const string HubPlannerSource = "HubPlanner";
    private const string BasePath = "/staffops/programme/connections";

    [HttpGet("")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var clickUp = await connectionAdmin.GetRowAsync(tenantId, ClickUpSource, "ClickUp");
        var hubPlanner = await connectionAdmin.GetRowAsync(tenantId, HubPlannerSource, "Hub Planner");

        ViewData["Title"] = "Connections";
        return View("~/Views/StaffOps/Programme/Connections.cshtml", new SourceConnectionsViewModel(clickUp, hubPlanner, message));
    }

    [HttpPost("clickup")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> SetClickUp([CurrentTenant] Guid tenantId, string apiToken, string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(workspaceId))
        {
            return RedirectWithMessage("Enter both a ClickUp API token and a workspace id.");
        }

        await SaveCredentialAsync(tenantId, ClickUpSource, new SourceCredential(apiToken, workspaceId));
        return RedirectWithMessage("ClickUp connection saved. The next sync will use this tenant's own workspace and token.");
    }

    [HttpPost("clickup/clear")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> ClearClickUp([CurrentTenant] Guid tenantId)
    {
        await ClearCredentialAsync(tenantId, ClickUpSource);
        return RedirectWithMessage("ClickUp connection cleared. Reconnect this tenant before syncing again.");
    }

    [HttpPost("hubplanner")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> SetHubPlanner([CurrentTenant] Guid tenantId, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RedirectWithMessage("Enter a Hub Planner API key.");
        }

        await SaveCredentialAsync(tenantId, HubPlannerSource, new SourceCredential(apiKey));
        return RedirectWithMessage("Hub Planner connection saved. The next sync will use this tenant's own account.");
    }

    [HttpPost("hubplanner/clear")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> ClearHubPlanner([CurrentTenant] Guid tenantId)
    {
        await ClearCredentialAsync(tenantId, HubPlannerSource);
        return RedirectWithMessage("Hub Planner connection cleared. Reconnect this tenant before syncing again.");
    }

    private async Task SaveCredentialAsync(Guid tenantId, string source, SourceCredential credential)
    {
        await connectionAdmin.SaveCredentialAsync(tenantId, source, credential, await currentStaff.GetMemberIdAsync());
        Services.Security.SecurityEvents.Write(logger, HttpContext, "SourceCredentialSet", tenantId: tenantId, provider: source, warning: false);
    }

    private async Task ClearCredentialAsync(Guid tenantId, string source)
    {
        await connectionAdmin.ClearCredentialAsync(tenantId, source, await currentStaff.GetMemberIdAsync());
        Services.Security.SecurityEvents.Write(logger, HttpContext, "SourceCredentialCleared", tenantId: tenantId, provider: source);
    }

    private IActionResult RedirectWithMessage(string message) =>
        Redirect($"{BasePath}?message={Uri.EscapeDataString(message)}");
}
