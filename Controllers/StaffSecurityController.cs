using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.SecurityAssurance;
using ProgrammePulse.Services.Integrations.Aikido;
using ProgrammePulse.Services.SecurityAssurance;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The tenant's security-tool connection (Aikido first): connect with a
/// read-only API client, disconnect, and sync. What it reads feeds the
/// contract assurance pages; this page only shows the connection and a
/// summary. Reports, never enforces (decision 1).
///
/// Plan-gated on <c>SecurityAssurance</c>; connecting is
/// <c>ManageIntegrations</c> and running a sync <c>TriggerSync</c>, the same
/// split as the support-desk connection.
/// </summary>
[Route("staffops/security")]
[RequireFeature(ProductFeature.SecurityAssurance)]
[RequireModule(ProductModules.Contracts)]
public sealed class StaffSecurityController(
    ICurrentStaff currentStaff,
    ISecurityAssuranceQueryService query,
    IAikidoConnectionService connections,
    AikidoIngestionService ingestion,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : Controller
{
    private const string IndexPath = "/staffops/security";

    /// <summary>How far back the page summarises pull-request check runs.</summary>
    private const int CheckRunWindowDays = 90;

    [HttpGet("")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-CheckRunWindowDays);
        var model = new SecurityToolsPageViewModel(
            await query.GetConnectionAsync(tenantId, SecurityTools.Aikido),
            await query.GetSnapshotAsync(tenantId, since),
            AikidoRegions.All.ToList());

        ViewData["Title"] = "Security tools";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Security/Index.cshtml", model);
    }

    [HttpPost("aikido")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Connect(string? region, string? clientId, string? clientSecret, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await connections.ConnectAsync(tenantId, staff.StaffKey, staff.MemberId, region, clientId, clientSecret, cancellationToken));
    }

    [HttpPost("aikido/disconnect")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await connections.DisconnectAsync(tenantId, staff.MemberId, cancellationToken));
    }

    [HttpPost("aikido/sync")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("sync")]
    [RequireCapability(Capability.TriggerSync)]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await ingestion.RunAsync(tenantId, staff.MemberId, cancellationToken));
    }

    private IActionResult ToResponse(CommandResult result) =>
        result.Status == CommandStatus.NotFound
            ? NotFound()
            : Redirect($"{IndexPath}?message=" + Uri.EscapeDataString(result.Message ?? string.Empty));
}
