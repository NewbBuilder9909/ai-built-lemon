using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

[Route("staffops/skills/examples")]
[RequireFeature(ProductFeature.GitHubEvidence)]
[RequireModule(ProductModules.Skills)]
public sealed class StaffContributionExamplesController(
    ContributionReviewService service, ICurrentStaff currentStaff, IStaffAuthorizationService authorization) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewOwnSkills)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, int page = 1, int sourcePage = 1)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        if (!ModelState.IsValid || page is < 1 or > 10000 || sourcePage is < 1 or > 10000) return BadRequest();
        return PageView(await OwnPageAsync(caller.StaffKey, tenantId, page, sourcePage));
    }

    [HttpGet("staff/{staffKey:guid}")]
    [RequireCapability(Capability.ViewStaffSkillEvidence)]
    public async Task<IActionResult> Staff([CurrentTenant] Guid tenantId, Guid staffKey, int page = 1)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        if (page is < 1 or > 10000) return BadRequest();
        if (caller.StaffKey == staffKey) return RedirectToAction(nameof(Index));
        return PageView(await service.GetPageAsync(staffKey, tenantId, page));
    }

    [HttpGet("{linkKey:guid}")]
    [RequireCapability(Capability.ViewOwnSkills)]
    public async Task<IActionResult> Detail([CurrentTenant] Guid tenantId, Guid linkKey)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        var detail = await DetailModelAsync(linkKey, caller.StaffKey, tenantId);
        return detail is null ? Forbid() : DetailView(detail);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Submit([CurrentTenant] Guid tenantId, [Bind(Prefix = "Form")] ContributionExampleForm form)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        try
        {
            CheckInput();
            var key = await service.SubmitAsync(caller.StaffKey, tenantId, form.AssertionKey, form.EvidenceKey,
                form.DemonstrationNote, form.AiTool, form.AiWorkflow);
            return RedirectToAction(nameof(Detail), new { linkKey = key });
        }
        catch (SkillAssertionValidationException ex)
        {
            return PageView((await OwnPageAsync(caller.StaffKey, tenantId, 1, Math.Clamp(form.SourcePage, 1, 10000))) with { Form = form, Error = ex.Message }, 400);
        }
    }

    [HttpPost("{linkKey:guid}/revise")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Revise([CurrentTenant] Guid tenantId, Guid linkKey, [Bind(Prefix = "Form")] ContributionExampleForm form)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        try
        {
            CheckInput();
            await service.ReviseAsync(linkKey, caller.StaffKey, tenantId, form.Revision, form.DemonstrationNote, form.AiTool, form.AiWorkflow);
            return RedirectToAction(nameof(Detail), new { linkKey });
        }
        catch (Exception ex) when (ex is SkillAssertionValidationException or ContributionReviewConflictException)
        {
            return await DetailErrorAsync(linkKey, caller.StaffKey, tenantId, ex, form, null);
        }
    }

    [HttpPost("{linkKey:guid}/withdraw")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.DeclareOwnSkills)]
    public async Task<IActionResult> Withdraw([CurrentTenant] Guid tenantId, Guid linkKey, int revision)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        try
        {
            CheckInput();
            await service.WithdrawAsync(linkKey, caller.StaffKey, tenantId, revision);
            return RedirectToAction(nameof(Detail), new { linkKey });
        }
        catch (Exception ex) when (ex is SkillAssertionValidationException or ContributionReviewConflictException)
        {
            return await DetailErrorAsync(linkKey, caller.StaffKey, tenantId, ex, null, null);
        }
    }

    [HttpPost("{linkKey:guid}/decide")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ReviewStaffSkills)]
    public async Task<IActionResult> Decide([CurrentTenant] Guid tenantId, Guid linkKey, [Bind(Prefix = "Decision")] ContributionDecisionForm form)
    {
        if (await CallerAsync(tenantId) is not { } caller) return Forbid();
        try
        {
            CheckInput();
            await service.DecideAsync(linkKey, caller.StaffKey, tenantId, form.Revision, form.Status, form.Note);
            return RedirectToAction(nameof(Detail), new { linkKey });
        }
        catch (Exception ex) when (ex is SkillAssertionValidationException or ContributionReviewConflictException)
        {
            return await DetailErrorAsync(linkKey, caller.StaffKey, tenantId, ex, null, form);
        }
    }

    private async Task<StaffProfile?> CallerAsync(Guid tenantId)
    {
        var caller = await currentStaff.GetProfileAsync();
        return caller is { IsActive: true } && caller.TenantId == tenantId ? caller : null;
    }

    private async Task<ContributionExamplesPage> OwnPageAsync(Guid staffKey, Guid tenantId, int page, int sourcePage = 1) =>
        (await service.GetPageAsync(staffKey, tenantId, page, sourcePage)) with
        { IsOwn = true, CanDeclare = await authorization.HasAsync(Capability.DeclareOwnSkills) };

    private async Task<ContributionExampleDetail?> DetailModelAsync(Guid linkKey, Guid caller, Guid tenantId)
    {
        var detail = await service.GetDetailAsync(linkKey, tenantId);
        var own = detail.Current.StaffKey == caller;
        if (!own && !await authorization.HasAsync(Capability.ViewStaffSkillEvidence)) return null;
        return detail with
        {
            IsOwn = own, CanDeclare = own && await authorization.HasAsync(Capability.DeclareOwnSkills),
            CanReview = !own && await authorization.HasAsync(Capability.ReviewStaffSkills),
            Form = new() { Revision = detail.Current.Revision, DemonstrationNote = detail.Current.DemonstrationNote,
                AiTool = detail.Current.AiTool, AiWorkflow = detail.Current.AiWorkflow },
            Decision = new() { Revision = detail.Current.Revision }
        };
    }

    private async Task<IActionResult> DetailErrorAsync(Guid key, Guid caller, Guid tenantId, Exception error,
        ContributionExampleForm? form, ContributionDecisionForm? decision)
    {
        var detail = await DetailModelAsync(key, caller, tenantId);
        if (detail is null) return Forbid();
        // Preserve the submitted revision on errors, including 409. Reload is
        // required to accept newer state; never silently rebase a stale decision.
        return DetailView(detail with { Error = error.Message, Form = form ?? detail.Form, Decision = decision ?? detail.Decision },
            error is ContributionReviewConflictException ? 409 : 400);
    }

    private void CheckInput()
    {
        if (!ModelState.IsValid) throw new SkillAssertionValidationException("Check the highlighted fields and choose valid options.");
    }

    private ViewResult PageView(ContributionExamplesPage model, int status = 200)
    {
        ViewData["Title"] = "Skill contribution examples";
        var view = View("~/Views/StaffOps/Skills/Examples/Index.cshtml", model);
        view.StatusCode = status;
        return view;
    }

    private ViewResult DetailView(ContributionExampleDetail model, int status = 200)
    {
        ViewData["Title"] = "Review a contribution example";
        var view = View("~/Views/StaffOps/Skills/Examples/Detail.cshtml", model);
        view.StatusCode = status;
        return view;
    }
}
