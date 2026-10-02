using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.ServiceOps;
using ProgrammePulse.Services.Integrations.Freshdesk;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ServiceOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Service health: support demand, recurrence and resolution by
/// component, the desk connection that feeds it, and the reviewed
/// links between cases and code.
///
/// Three audiences, three capabilities, and the split is the point:
///
/// - <c>ViewServiceHealth</c> is the component view. Deliberately wider
///   than the skills capabilities — Analyst and Board hold it — because
///   support demand is about a product and its owning team, not about
///   employees.
/// - <c>ReviewRootCause</c> is the only way to say a change caused a
///   case. Team Lead and Admin.
/// - <c>ManageIntegrations</c> connects the desk;
///   <c>ManageIdentityMappings</c> approves which agent is which person.
///   Both reused rather than duplicated.
///
/// The desk operations and their audit trail live in IDeskAdminService and
/// IFreshdeskConnectionService; root-cause review in ISupportCodeLinkService.
///
/// Plan-gated on <c>SupportEvidence</c>, separately from
/// <c>GitHubEvidence</c>, so a customer really can buy and use one
/// without the other.
/// </summary>
[Route("staffops/service")]
[RequireFeature(ProductFeature.SupportEvidence)]
[RequireModule(ProductModules.ServiceHealth)]
public sealed class StaffServiceController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    IServiceHealthQueryService serviceHealthQueryService,
    ISupportCodeLinkService supportCodeLinkService,
    IDeskAdminService desks,
    IFreshdeskConnectionService freshdeskConnections,
    FreshdeskIngestionService ingestion,
    IIntegrationRunStatusQueryService runStatus,
    IOptions<ServiceOpsOptions> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : Controller
{
    private const string DesksPath = "/staffops/service/desks";
    private const string LinksPath = "/staffops/service/links";
    private const string AgentsPath = "/staffops/service/agents";

    [HttpGet("")]
    [RequireCapability(Capability.ViewServiceHealth)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, DateOnly? from = null, DateOnly? to = null, string? message = null)
    {
        // A period is required for every comparison, so there is no
        // "all time" — an unbounded window makes trends meaningless, and
        // ReportingPeriod bounds what a crafted query string can ask for.
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (!ReportingPeriod.TryResolve(from, to, today.AddDays(1), options.Value.DefaultReportingWindowDays, today,
                out var periodStart, out var periodEnd, out var periodError))
        {
            return Redirect("/staffops/service?message=" + Uri.EscapeDataString(periodError!));
        }

        ServiceHealthReport report;
        try
        {
            report = await serviceHealthQueryService.BuildAsync(tenantId, periodStart, periodEnd);
        }
        catch (ServiceOpsValidationException ex)
        {
            return Redirect("/staffops/service?message=" + Uri.EscapeDataString(ex.Message));
        }

        var model = new ServiceHealthPageViewModel(report,
            await staffAuthorizationService.HasAsync(Capability.ReviewRootCause),
            await staffAuthorizationService.HasAsync(Capability.ManageIntegrations));

        ViewData["Title"] = "Service Health";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Service/Index.cshtml", model);
    }

    // ---- Desk connection ----

    [HttpGet("desks")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Desks([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await desks.BuildDesksPageAsync(tenantId) with
        {
            RunStates = await runStatus.GetStatesAsync(tenantId, [(FreshdeskIngestionService.SourceName, "Freshdesk service health")]),
        };

        ViewData["Title"] = "Support desks";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Service/Desks.cshtml", model);
    }

    /// <summary>
    /// Connects a Freshdesk account with an API key, verified against the
    /// desk before it is stored (IFreshdeskConnectionService).
    /// </summary>
    [HttpPost("desks/freshdesk")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> ConnectFreshdesk(string account, string apiToken, string? approvedComponents, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await freshdeskConnections.ConnectAsync(tenantId, staff.StaffKey, staff.MemberId, account, apiToken, approvedComponents, cancellationToken), DesksPath);
    }

    [HttpPost("desks/{connectionKey:guid}/components")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> SetComponents(Guid connectionKey, string? approvedComponents)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await desks.SetComponentsAsync(tenantId, connectionKey, approvedComponents, staff.MemberId), DesksPath);
    }

    [HttpPost("desks/{connectionKey:guid}/sync")]
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
            return Back(result.Summary, DesksPath);
        }
        catch (ServiceOpsValidationException ex)
        {
            return Back(ex.Message, DesksPath);
        }
    }

    [HttpPost("desks/{connectionKey:guid}/disconnect")]
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
        return Back(await desks.DisconnectAsync(tenantId, connectionKey, staff.MemberId), DesksPath);
    }

    // ---- Root-cause review ----

    [HttpGet("links")]
    [RequireCapability(Capability.ViewServiceHealth)]
    public async Task<IActionResult> Links([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await desks.BuildLinksPageAsync(tenantId, await staffAuthorizationService.HasAsync(Capability.ReviewRootCause));

        ViewData["Title"] = "Case and code links";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Service/Links.cshtml", model);
    }

    [HttpPost("links/{linkKey:guid}/confirm")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewRootCause)]
    public async Task<IActionResult> ConfirmRootCause(Guid linkKey, string reviewNote)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (reviewer, tenantId) = context.Value;

        return await RunAsync(() => supportCodeLinkService.ConfirmRootCauseAsync(
            linkKey, reviewer.StaffKey, reviewNote, tenantId, reviewer.MemberId),
            "Root cause confirmed, with your assessment on the record.");
    }

    [HttpPost("links/{linkKey:guid}/rule-out")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewRootCause)]
    public async Task<IActionResult> RuleOut(Guid linkKey, string reviewNote)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (reviewer, tenantId) = context.Value;

        return await RunAsync(() => supportCodeLinkService.RuleOutAsync(
            linkKey, reviewer.StaffKey, reviewNote, tenantId, reviewer.MemberId),
            "Ruled out. It will not be suggested again.");
    }

    [HttpPost("links")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewRootCause)]
    public async Task<IActionResult> LinkManually(
        Guid connectionKey, string externalTicketId, LinkedArtifactType artifactType,
        string artifactExternalId, string? artifactSource, string? artifactUrl)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => supportCodeLinkService.LinkManuallyAsync(
            connectionKey, externalTicketId, artifactType, artifactExternalId,
            artifactSource, artifactUrl, actor.StaffKey, tenantId, actor.MemberId),
            "Linked. This records a relationship — confirm a root cause separately if the assessment supports it.");
    }

    // ---- Agent mapping ----

    [HttpGet("agents")]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> Agents([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await desks.BuildAgentsPageAsync(tenantId);

        ViewData["Title"] = "Unidentified desk agents";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Service/Agents.cshtml", model);
    }

    [HttpPost("agents/approve")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIdentityMappings)]
    public async Task<IActionResult> ApproveAgent(Guid connectionKey, string externalAgentId, string? agentName, Guid staffKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (approver, tenantId) = context.Value;
        return ToResponse(await desks.ApproveAgentAsync(tenantId, connectionKey, externalAgentId, agentName, staffKey, approver.StaffKey, approver.MemberId), AgentsPath);
    }

    // ---- helpers ----

    private async Task<IActionResult> RunAsync(Func<Task> action, string successMessage)
    {
        try
        {
            await action();
        }
        catch (ServiceOpsValidationException ex)
        {
            return Back(ex.Message, LinksPath);
        }

        return Back(successMessage, LinksPath);
    }

    private IActionResult ToResponse(CommandResult result, string path) =>
        result.Status == CommandStatus.NotFound ? NotFound() : Back(result.Message ?? string.Empty, path);

    private IActionResult Back(string message, string path) =>
        Redirect($"{path}?message=" + Uri.EscapeDataString(message));
}
