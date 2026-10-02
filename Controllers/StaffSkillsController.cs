using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.Shared;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The reviewed skills matrix. Four audiences, four capabilities, and the
/// split between them is the point of the feature:
///
/// - anyone signed in sees and corrects *their own* record
///   (ViewOwnSkills / DeclareOwnSkills);
/// - a reviewer sees and decides *other people's*
///   (ViewStaffSkillEvidence / ReviewStaffSkills);
/// - the coverage page is the aggregate, no person in the answer
///   (ViewTeamSkillCoverage);
/// - only an Admin changes the taxonomy everyone is measured against
///   (ManageSkillTaxonomy).
///
/// Every action gets its tenant from a [CurrentTenant] parameter or, for
/// self-service actions, from ResolveSelfAsync, and forbids an unresolved
/// one, the same shape as StaffContractController. Nothing here is plan-gated yet — see
/// docs/staff-skills-evidence-module.md's delivery gates; a ProductFeature
/// for this is deliberately left for the connector slices, when there is
/// something to sell separately.
///
/// No action on this controller touches StaffRate, and none can: the query
/// services it calls do not fetch rates (CLAUDE.md, "Cost/rate data
/// isolation is structural").
/// </summary>
[Route("staffops/skills")]
[RequireModule(ProductModules.Skills)]
public sealed class StaffSkillsController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    ISkillsPageService skillsPages,
    ISkillAssertionService skillAssertionService,
    ISkillCoverageQueryService skillCoverageQueryService,
    Services.ServiceOps.ISupportParticipationQueryService supportParticipation,
    ITenantContext tenantContext) : Controller
{
    // ---- Self-service ----

    [HttpGet("")]
    public async Task<IActionResult> Index(string? message = null, int page = 1)
    {
        if (!await staffAuthorizationService.HasAsync(Capability.ViewOwnSkills))
        {
            return Redirect("/staffops/account/login");
        }

        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        // Skills and support participation come from separate areas that
        // must not depend on each other; the page is where they meet.
        var model = await skillsPages.BuildMySkillsAsync(staff.StaffKey, tenantId, new PageRequest(page)) with
        {
            OwnSupportParticipation = await supportParticipation.GetOwnParticipationAsync(staff.StaffKey, tenantId),
            CanReview = await staffAuthorizationService.HasAsync(Capability.ReviewStaffSkills)
        };

        ViewData["Title"] = "My Skills";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Index.cshtml", model);
    }

    [HttpPost("declare")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Declare(string skillKey, ProficiencyLevel proficiency, string? evidenceNote)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.DeclareAsync(
            staff.StaffKey, skillKey, proficiency, evidenceNote, tenantId, MemberIdOf(staff)),
            "/staffops/skills", "Skill declared. It is waiting for review.");
    }

    [HttpPost("{assertionKey:guid}/challenge")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Challenge(Guid assertionKey, ProficiencyLevel claimedProficiency, string reason)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.ChallengeAsync(
            assertionKey, staff.StaffKey, claimedProficiency, reason, tenantId, MemberIdOf(staff)),
            "/staffops/skills", "Correction raised. It is back with a reviewer.");
    }

    [HttpPost("{assertionKey:guid}/withdraw")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Withdraw(Guid assertionKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (staff, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.WithdrawAsync(
            assertionKey, staff.StaffKey, tenantId, MemberIdOf(staff)),
            "/staffops/skills", "Skill withdrawn.");
    }

    // ---- Review ----

    [HttpGet("review")]
    [RequireCapability(Capability.ViewStaffSkillEvidence)]
    public async Task<IActionResult> Review([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await skillsPages.BuildReviewQueueAsync(tenantId) with
        {
            CanDecide = await staffAuthorizationService.HasAsync(Capability.ReviewStaffSkills)
        };

        ViewData["Title"] = "Skills Review Queue";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Review.cshtml", model);
    }

    [HttpPost("{assertionKey:guid}/validate")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewStaffSkills)]
    public async Task<IActionResult> Validate(Guid assertionKey, ProficiencyLevel proficiency, string reviewNote, DateOnly? reviewDueOn)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (reviewer, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.ValidateAsync(
            assertionKey, reviewer.StaffKey, proficiency, reviewNote, reviewDueOn, tenantId, MemberIdOf(reviewer)),
            "/staffops/skills/review", "Validated.");
    }

    [HttpPost("{assertionKey:guid}/reject")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewStaffSkills)]
    public async Task<IActionResult> Reject(Guid assertionKey, string reviewNote)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (reviewer, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.RejectAsync(
            assertionKey, reviewer.StaffKey, reviewNote, tenantId, MemberIdOf(reviewer)),
            "/staffops/skills/review", "Rejected, with your reason on the record.");
    }

    [HttpGet("staff/{staffKey:guid}")]
    [RequireCapability(Capability.ViewStaffSkillEvidence)]
    public async Task<IActionResult> Portfolio([CurrentTenant] Guid tenantId, Guid staffKey, int page = 1)
    {
        // A staff key from another tenant is a 404 rather than a portfolio.
        var portfolio = await skillsPages.BuildPortfolioAsync(tenantId, staffKey, new PageRequest(page));
        if (portfolio is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Skills — {portfolio.StaffName}";
        return View("~/Views/StaffOps/Skills/Portfolio.cshtml", portfolio with
        {
            CanDecide = await staffAuthorizationService.HasAsync(Capability.ReviewStaffSkills)
        });
    }

    [HttpPost("staff/{staffKey:guid}/record")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewStaffSkills)]
    public async Task<IActionResult> RecordForStaff(Guid staffKey, string skillKey, ProficiencyLevel proficiency, string reviewNote, DateOnly? reviewDueOn)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (reviewer, tenantId) = context.Value;

        if (!await skillsPages.IsOnRosterAsync(tenantId, staffKey))
        {
            return NotFound();
        }

        return await RunAsync(() => skillAssertionService.RecordForStaffAsync(
            staffKey, reviewer.StaffKey, skillKey, proficiency, reviewNote, reviewDueOn, tenantId, MemberIdOf(reviewer)),
            $"/staffops/skills/staff/{staffKey}", "Recorded.");
    }

    // ---- Coverage (aggregate) ----

    [HttpGet("coverage")]
    [RequireCapability(Capability.ViewTeamSkillCoverage)]
    public async Task<IActionResult> Coverage([CurrentTenant] Guid tenantId)
    {
        var report = await skillCoverageQueryService.BuildAsync(tenantId);

        ViewData["Title"] = "Skills Coverage";
        return View("~/Views/StaffOps/Skills/Coverage.cshtml", report);
    }

    // ---- Taxonomy (Admin) ----

    [HttpGet("taxonomy")]
    [RequireCapability(Capability.ManageSkillTaxonomy)]
    public async Task<IActionResult> Taxonomy([CurrentTenant] Guid tenantId, string? message = null)
    {
        var skills = await skillsPages.GetTaxonomyAsync(tenantId);

        ViewData["Title"] = "Skill Taxonomy";
        ViewData["Message"] = message;

        return View("~/Views/StaffOps/Skills/Taxonomy.cshtml", skills);
    }

    [HttpPost("taxonomy")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageSkillTaxonomy)]
    public async Task<IActionResult> CreateSkill(string name, string? skillKey, SkillKind kind, string? description)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.CreateSkillAsync(
            skillKey ?? string.Empty, name, kind, description, tenantId, MemberIdOf(actor)),
            "/staffops/skills/taxonomy", "Skill added.");
    }

    [HttpPost("taxonomy/{skillKey}/active")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageSkillTaxonomy)]
    public async Task<IActionResult> SetSkillActive(string skillKey, bool isActive)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => skillAssertionService.SetSkillActiveAsync(
            skillKey, isActive, tenantId, MemberIdOf(actor)),
            "/staffops/skills/taxonomy", isActive ? "Skill reinstated." : "Skill retired.");
    }

    // ---- helpers ----

    /// <summary>
    /// Runs a domain call and turns its two failure modes into the two
    /// different answers they deserve: a validation problem is a message on
    /// the user's own page, a cross-tenant or unknown key is a 404 — so a
    /// key belonging to another tenant is indistinguishable from one that
    /// never existed.
    /// </summary>
    private async Task<IActionResult> RunAsync(Func<Task> action, string returnPath, string successMessage)
    {
        try
        {
            await action();
        }
        catch (SkillAssertionValidationException ex)
        {
            return Redirect($"{returnPath}?message=" + Uri.EscapeDataString(ex.Message));
        }

        return Redirect($"{returnPath}?message=" + Uri.EscapeDataString(successMessage));
    }

    private static int? MemberIdOf(StaffProfile staff) => staff.MemberId;
}
