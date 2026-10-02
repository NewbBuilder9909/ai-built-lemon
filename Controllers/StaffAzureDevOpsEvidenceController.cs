using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.AzureDevOps;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Tenant-admin connection of an Azure DevOps Services organisation as an
/// engineering evidence source: verify a read-only personal access token,
/// choose repositories from the ones it can actually read, and run a sync.
/// The flow and its refusals live in IAzureDevOpsConnectionService; this
/// controller maps them to HTTP.
///
/// Disconnecting and the identity-mapping queue are provider-neutral and
/// stay on StaffEvidenceConnectionController; only what is specific to
/// Azure DevOps lives here, so the GitHub flow is untouched.
///
/// Why a token rather than OAuth: Azure DevOps' own OAuth no longer
/// accepts new app registrations, and its successor (Microsoft Entra ID)
/// is a larger identity decision this slice does not take. A token scoped
/// to Code (Read), verified before it is stored, encrypted, never shown
/// again and never replaced by a deployment-wide fallback, is the
/// pilot-grade route — the same one Freshdesk uses.
///
/// Plan-gated on <see cref="ProductFeature.GitHubEvidence"/>: the flag
/// governs person-level repository evidence, whichever forge it comes
/// from, and a second flag would let a tenant collect the same category of
/// data under a different commercial switch.
/// </summary>
[Route("staffops/skills/evidence/azure-devops")]
[RequireFeature(ProductFeature.GitHubEvidence)]
[RequireModule(ProductModules.Skills)]
public sealed class StaffAzureDevOpsEvidenceController(
    ICurrentStaff currentStaff,
    IAzureDevOpsConnectionService connections,
    AzureDevOpsEvidenceIngestionService ingestion,
    ITenantContext tenantContext) : Controller
{
    private const string EvidenceHome = "/staffops/skills/evidence";

    [HttpPost("connect")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Connect(string organisation, string accessToken, DateOnly? expiresOn, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        var result = await connections.ConnectAsync(tenantId, staff.StaffKey, staff.MemberId, organisation, accessToken, expiresOn, cancellationToken);

        return result.Status == CommandStatus.Succeeded
            ? Redirect($"{EvidenceHome}/azure-devops/{result.ConnectionKey}/repositories")
            : ToResponse(result);
    }

    [HttpGet("{connectionKey:guid}/repositories")]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> Repositories(Guid connectionKey, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (model, problem) = await connections.BuildRepositoryChoiceAsync(context.Value.TenantId, connectionKey, cancellationToken);
        if (problem is not null)
        {
            return ToResponse(problem);
        }

        ViewData["Title"] = "Choose Azure DevOps repositories";
        return View("~/Views/StaffOps/Skills/Evidence/AzureDevOpsRepositories.cshtml", model);
    }

    [HttpPost("{connectionKey:guid}/repositories")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageIntegrations)]
    public async Task<IActionResult> SaveRepositories(Guid connectionKey, string[]? repositories, CancellationToken cancellationToken)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;
        return ToResponse(await connections.SaveRepositoriesAsync(tenantId, connectionKey, repositories, staff.MemberId, cancellationToken));
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

    private IActionResult ToResponse(CommandResult result) =>
        result.Status == CommandStatus.NotFound ? NotFound() : Back(result.Message ?? string.Empty);

    private IActionResult Back(string message) => Redirect($"{EvidenceHome}?message=" + Uri.EscapeDataString(message));
}
