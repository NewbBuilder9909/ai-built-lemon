using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using Microsoft.AspNetCore.WebUtilities;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The continuity plan: who owns each component, who is approved to
/// cover them, what is being done about the gaps, and — separately —
/// the tenant's recorded basis for collecting person-level evidence at
/// all.
///
/// Reading the plan needs <c>ViewStaffSkillEvidence</c> rather than the
/// wider <c>ViewTeamSkillCoverage</c>, because unlike the aggregate
/// coverage view this one names people: a continuity plan whose owner is
/// anonymous is not a plan. Changing it needs
/// <c>ManageContinuityPlan</c>.
///
/// The processing-decision page is deliberately a separate capability
/// held only by Admin. It is a legal attestation with a named signatory,
/// and it gates whether evidence collection may run — a delivery manager
/// should not be able to unblock collection of their own team's activity.
///
/// **Not plan-gated.** The continuity plan is a management artefact that
/// works with nothing connected: a tenant on any plan can map components
/// and record cover. Only the evidence that can enrich it is gated.
/// </summary>
[Route("staffops/skills/continuity")]
[RequireModule(ProductModules.Skills)]
public sealed class StaffContinuityController(
    ICurrentStaff currentStaff,
    IStaffAuthorizationService staffAuthorizationService,
    IContinuityService continuityService,
    IContinuityPageService pages,
    ISuggestionService suggestionService,
    ITenantContext tenantContext) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewStaffSkillEvidence)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await pages.BuildPlanPageAsync(tenantId,
            await staffAuthorizationService.HasAsync(Capability.ManageContinuityPlan),
            await staffAuthorizationService.HasAsync(Capability.RecordProcessingDecision));

        ViewData["Title"] = "Key-person coverage";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Continuity/Index.cshtml", model);
    }

    [HttpPost("components")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> DeclareComponent(string displayName, string? componentKey, string? description, Guid? ownerStaffKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        if (ownerStaffKey is { } owner && !await pages.IsOnRosterAsync(tenantId, owner))
        {
            return NotFound();
        }

        return await RunAsync(() => continuityService.DeclareComponentAsync(
            componentKey ?? string.Empty, displayName, description, ownerStaffKey,
            actor.StaffKey, tenantId, actor.MemberId),
            "Component recorded, and marked reviewed today.");
    }

    [HttpPost("components/{componentKey}/confirm")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> ConfirmComponent(string componentKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.ConfirmComponentAsync(
            componentKey, actor.StaffKey, tenantId, actor.MemberId),
            "Confirmed as still current.");
    }

    [HttpPost("components/{componentKey}/retire")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> RetireComponent(string componentKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.RetireComponentAsync(componentKey, tenantId, actor.MemberId),
            "Component retired, with its approved cover.");
    }

    [HttpPost("components/{componentKey}/backups")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> ApproveBackup(string componentKey, Guid staffKey, string? note)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        if (!await pages.IsOnRosterAsync(tenantId, staffKey))
        {
            return NotFound();
        }

        return await RunAsync(() => continuityService.ApproveBackupAsync(
            componentKey, staffKey, note, actor.StaffKey, tenantId, actor.MemberId),
            "Approved as cover.");
    }

    [HttpPost("backups/{backupKey:guid}/remove")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> RemoveBackup(Guid backupKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.RemoveBackupAsync(backupKey, tenantId, actor.MemberId),
            "Cover removed.");
    }

    [HttpPost("actions")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> RaiseAction(
        string componentKey, CoverageActionType type, Guid ownerStaffKey, string rationale,
        string? evidenceSnapshot, DateOnly? dueOn)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        if (!await pages.IsOnRosterAsync(tenantId, ownerStaffKey))
        {
            return NotFound();
        }

        return await RunAsync(() => continuityService.RaiseActionAsync(
            componentKey, type, ownerStaffKey, rationale, evidenceSnapshot, dueOn,
            actor.StaffKey, tenantId, actor.MemberId),
            "Action raised, with an owner against it.");
    }

    [HttpPost("actions/{actionKey:guid}/close")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> CloseAction(Guid actionKey, CoverageActionOutcome outcome, string outcomeNote)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.CloseActionAsync(
            actionKey, outcome, outcomeNote, actor.StaffKey, tenantId, actor.MemberId),
            "Closed, with the outcome on the record.");
    }

    // ---- Suggestions (Slice 5) ----

    /// <summary>
    /// The proposal queue. Behind <c>ViewStaffSkillEvidence</c> — the
    /// person-level grant — because a skill-tag suggestion names an
    /// individual and is derived from evidence about them. Deciding
    /// needs <c>ManageContinuityPlan</c>, the same grant that reviews
    /// the things a suggestion can turn into.
    /// </summary>
    [HttpGet("suggestions")]
    [RequireCapability(Capability.ViewStaffSkillEvidence)]
    public async Task<IActionResult> Suggestions([CurrentTenant] Guid tenantId, string? message = null)
    {
        var model = await pages.BuildSuggestionsPageAsync(tenantId,
            await staffAuthorizationService.HasAsync(Capability.ManageContinuityPlan));

        ViewData["Title"] = "Suggestions";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Continuity/Suggestions.cshtml", model);
    }

    [HttpPost("suggestions/generate")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> GenerateSuggestions()
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;
        var result = await suggestionService.GenerateAsync(tenantId, actor.MemberId);

        return Back(result.Summary, ContinuityBackTarget.Suggestions);
    }

    [HttpPost("suggestions/{suggestionKey:guid}/accept")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> AcceptSuggestion(Guid suggestionKey, Guid? actionOwnerStaffKey)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        if (actionOwnerStaffKey is { } owner && !await pages.IsOnRosterAsync(tenantId, owner))
        {
            return NotFound();
        }

        try
        {
            var created = await suggestionService.AcceptAsync(
                suggestionKey, actor.StaffKey, actionOwnerStaffKey, tenantId, actor.MemberId);
            return Back(created, ContinuityBackTarget.Suggestions);
        }
        catch (SkillAssertionValidationException ex)
        {
            return Back(ex.Message, ContinuityBackTarget.Suggestions);
        }
    }

    [HttpPost("suggestions/{suggestionKey:guid}/dismiss")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ManageContinuityPlan)]
    public async Task<IActionResult> DismissSuggestion(Guid suggestionKey, string? note)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => suggestionService.DismissAsync(
            suggestionKey, actor.StaffKey, note, tenantId, actor.MemberId),
            "Dismissed. It will not be raised again.",
            ContinuityBackTarget.Suggestions);
    }

    // ---- Data-processing decision and enablement readiness ----

    [HttpGet("processing")]
    [RequireCapability(Capability.RecordProcessingDecision)]
    public async Task<IActionResult> Processing([CurrentTenant] Guid tenantId, string? message = null)
    {
        ViewData["Title"] = "Evidence collection: basis and readiness";
        ViewData["Message"] = message;
        return View("~/Views/StaffOps/Skills/Continuity/Processing.cshtml", await pages.BuildProcessingPageAsync(tenantId));
    }

    [HttpPost("processing")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.RecordProcessingDecision)]
    public async Task<IActionResult> RecordProcessing(
        EvidenceLawfulBasis lawfulBasis, bool workerNoticeGiven, string? workerNoticeReference,
        bool dpiaCompleted, string? dpiaReference, DateOnly? dpiaCompletedOn, string purpose, DateOnly reviewDueOn)
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.RecordProcessingDecisionAsync(
            lawfulBasis, workerNoticeGiven, workerNoticeReference, dpiaCompleted, dpiaReference,
            dpiaCompletedOn?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), purpose, reviewDueOn,
            actor.StaffKey, tenantId, actor.MemberId),
            "Recorded. Any previous decision has been superseded and kept in the history.",
            ContinuityBackTarget.Processing);
    }

    [HttpPost("processing/withdraw")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.RecordProcessingDecision)]
    public async Task<IActionResult> WithdrawProcessing()
    {
        var context = await tenantContext.ResolveSelfAsync(currentStaff);
        if (context is null)
        {
            return Forbid();
        }

        var (actor, tenantId) = context.Value;

        return await RunAsync(() => continuityService.WithdrawProcessingDecisionAsync(tenantId, actor.MemberId),
            "Withdrawn. Person-level evidence collection is blocked until a new decision is recorded.",
            ContinuityBackTarget.Processing);
    }

    // ---- helpers ----

    private async Task<IActionResult> RunAsync(Func<Task> action, string successMessage, ContinuityBackTarget target = ContinuityBackTarget.Index)
    {
        try
        {
            await action();
        }
        catch (SkillAssertionValidationException ex)
        {
            return Back(ex.Message, target);
        }

        return Back(successMessage, target);
    }

    // A fixed set of local targets and LocalRedirect: no message or path from
    // the request can ever turn this into an off-site redirect (PR #14).
    private IActionResult Back(string message, ContinuityBackTarget target = ContinuityBackTarget.Index) =>
        LocalRedirect(QueryHelpers.AddQueryString(
            target switch
            {
                ContinuityBackTarget.Suggestions => "/staffops/skills/continuity/suggestions",
                ContinuityBackTarget.Processing => "/staffops/skills/continuity/processing",
                _ => "/staffops/skills/continuity",
            },
            "message",
            message));

    private enum ContinuityBackTarget
    {
        Index,
        Suggestions,
        Processing,
    }
}
