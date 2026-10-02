using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Declared repository → project links (step 1 of
/// docs/delivery-evidence-and-contract-assurance.md). ManageCustomers: the
/// same people who decide which programme belongs to which customer, since a
/// link decides which customer's effort and contractual obligations a
/// repository's work counts towards.
///
/// The gap list ("repositories the evidence sources read that no project
/// claims") comes from the evidence area. Programme Ops may not depend on it,
/// so this controller composes the two, as the skills page does with support
/// participation. When the evidence feature is off, the gap list is empty and
/// the page says why.
/// </summary>
[Route("staffops/programme/repositories")]
[RequireModule(ProductModules.Contracts)]
public sealed class StaffCodeRepositoryController(
    ICurrentStaff currentStaff,
    ICodeRepositoryLinkService repositoryLinks,
    IEvidenceSourceAdminService evidenceSources,
    IFeatureGate featureGate) : Controller
{
    private const string BasePath = "/staffops/programme/repositories";

    [HttpGet("")]
    [RequireCapability(Capability.ManageCustomers)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null, int page = 1)
    {
        var evidenceAvailable = await featureGate.IsEnabledAsync(ProductFeature.GitHubEvidence);
        var known = evidenceAvailable
            ? (await evidenceSources.GetSelectedRepositoriesAsync(tenantId))
                .Select(r => CodeRepositoryRef.TryCreate(r.Provider, r.SourceAccountId, r.RepositoryKey, out _))
                .OfType<CodeRepositoryRef>()
                .ToList()
            : [];

        var model = await repositoryLinks.BuildPageAsync(tenantId, new PageRequest(page), known, evidenceAvailable, message);

        ViewData["Title"] = "Code Repositories";
        return View("~/Views/StaffOps/Programme/Repositories.cshtml", model);
    }

    [HttpPost("link")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageCustomers)]
    public async Task<IActionResult> Link([CurrentTenant] Guid tenantId, string? provider, string? sourceAccountId, string? repositoryKey, Guid projectKey, string? note)
    {
        var caller = await currentStaff.GetProfileAsync();
        var outcome = await repositoryLinks.LinkAsync(tenantId, provider, sourceAccountId, repositoryKey, projectKey, note,
            caller?.StaffKey, await currentStaff.GetMemberIdAsync());

        return RedirectWithMessage(outcome.Succeeded ? "Linked." : outcome.Error!);
    }

    [HttpPost("unlink")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageCustomers)]
    public async Task<IActionResult> Unlink([CurrentTenant] Guid tenantId, Guid linkKey)
    {
        var caller = await currentStaff.GetProfileAsync();
        var outcome = await repositoryLinks.UnlinkAsync(tenantId, linkKey, caller?.StaffKey, await currentStaff.GetMemberIdAsync());

        return RedirectWithMessage(outcome.Succeeded ? "Link ended. It stays on record in the audit trail." : outcome.Error!);
    }

    private IActionResult RedirectWithMessage(string message) =>
        Redirect($"{BasePath}?message={Uri.EscapeDataString(message)}");
}
