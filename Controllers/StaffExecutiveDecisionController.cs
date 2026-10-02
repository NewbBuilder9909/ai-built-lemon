using System.ComponentModel.DataAnnotations;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Models.Tenancy;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ExecutiveReview;

namespace ProgrammePulse.Controllers;

[Route("staffops/executive/decisions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequireModule(ProductModules.ExecutiveReview)]
public sealed class StaffExecutiveDecisionController(ExecutiveDecisionService decisions,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("")]
    public Task<IActionResult> Index(DecisionStatus? status) => Guard(async () =>
    {
        if (!ModelState.IsValid) return BadRequest(localizer["Decision.InvalidFields"].Value);
        ViewData["Title"] = localizer["Decision.Queue"];
        return View("~/Views/StaffOps/ExecutiveReview/Decisions.cshtml", await decisions.GetQueueAsync(status));
    });

    [HttpGet("{decisionKey:guid}")]
    public Task<IActionResult> Detail(Guid decisionKey) => Guard(async () => DetailView(await decisions.GetDetailAsync(decisionKey)));

    [HttpGet("{decisionKey:guid}/download")]
    public Task<IActionResult> Download(Guid decisionKey) => Guard(async () =>
    {
        var detail = await decisions.GetDetailAsync(decisionKey);
        // No staff directory in the download; versioned actor/owner keys remain auditable.
        return File(JsonSerializer.SerializeToUtf8Bytes(detail.History), "application/json", $"decision-{decisionKey:N}.json");
    });

    [HttpGet("new")]
    public Task<IActionResult> New(Guid packKey) => Guard(async () => EditorView(await decisions.GetEditorAsync(packKey)));

    [HttpGet("{decisionKey:guid}/edit")]
    public Task<IActionResult> Edit(Guid decisionKey, Guid? packKey) => Guard(async () =>
        EditorView(await decisions.GetEditorAsync(packKey ?? Guid.Empty, decisionKey)));

    [HttpPost("create"), ValidateAntiForgeryToken]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] DecisionDraftInput input) => Guard(async () =>
    {
        var editor = await decisions.GetEditorAsync(input.PackKey ?? Guid.Empty);
        if (!ModelState.IsValid) return EditorView(editor with { Input = input, ErrorKey = "Decision.InvalidFields" }, 400);
        try
        {
            var revision = await decisions.CreateAsync(input.ToDefinition());
            return RedirectToAction(nameof(Detail), new { decisionKey = revision.DecisionKey });
        }
        catch (ReviewValidationException error) { return EditorView(editor with { Input = input, ErrorKey = error.Message }, 400); }
    });

    [HttpPost("{decisionKey:guid}/revise"), ValidateAntiForgeryToken]
    public Task<IActionResult> Revise(Guid decisionKey, [Bind(Prefix = "Input")] DecisionDraftInput input,
        [Required, Range(1, int.MaxValue)] int? expectedVersion, [Required, StringLength(2000)] string note) => Guard(async () =>
    {
        var editor = await decisions.GetEditorAsync(input.PackKey ?? Guid.Empty, decisionKey);
        if (!ModelState.IsValid) return EditorView(editor with { Input = input, ExpectedVersion = expectedVersion, ErrorKey = "Decision.InvalidFields" }, 400);
        try
        {
            await decisions.ApplyAsync(decisionKey, expectedVersion!.Value, new(DecisionAction.Revise, note, input.ToDefinition()));
            return RedirectToAction(nameof(Detail), new { decisionKey });
        }
        catch (ReviewConflictException) { return EditorView(editor with { Input = input, ExpectedVersion = expectedVersion, ErrorKey = "Decision.Conflict" }, 409); }
        catch (ReviewValidationException error) { return EditorView(editor with { Input = input, ExpectedVersion = expectedVersion, ErrorKey = error.Message }, 400); }
    });

    [HttpPost("{decisionKey:guid}/act"), ValidateAntiForgeryToken]
    public Task<IActionResult> Act(Guid decisionKey, DecisionActionInput input) => Guard(async () =>
    {
        var detail = await decisions.GetDetailAsync(decisionKey);
        // Authorisation precedes validation, including a forged action from a reader.
        var reviewAction = input.Action is DecisionAction.AcceptEvidence or DecisionAction.DisputeEvidence;
        if (reviewAction ? !detail.CanReview : !detail.CanManage) return Forbid();
        if (!ModelState.IsValid || input.Action == DecisionAction.Revise)
            return DetailView(detail with { ErrorKey = "Decision.InvalidFields", AttemptedAction = input }, 400);
        try
        {
            await decisions.ApplyAsync(decisionKey, input.ExpectedVersion!.Value, new(input.Action!.Value,
                input.Note, Decision: input.Decision, Rationale: input.Rationale, ActionOwnerStaffKey: input.ActionOwnerStaffKey,
                FollowUpOn: input.FollowUpOn, Outcome: input.Outcome));
            return RedirectToAction(nameof(Detail), new { decisionKey });
        }
        catch (ReviewConflictException) { return DetailView(detail with { ErrorKey = "Decision.Conflict", AttemptedAction = input }, 409); }
        catch (ReviewValidationException error) { return DetailView(detail with { ErrorKey = error.Message, AttemptedAction = input }, 400); }
    });

    private ViewResult EditorView(DecisionEditor model, int status = 200)
    {
        ViewData["Title"] = localizer[model.DecisionKey is null ? "Decision.New" : "Decision.Edit"];
        Response.StatusCode = status;
        return View("~/Views/StaffOps/ExecutiveReview/DecisionEditor.cshtml", model);
    }

    private ViewResult DetailView(DecisionDetail model, int status = 200)
    {
        ViewData["Title"] = localizer["Decision.Detail"];
        Response.StatusCode = status;
        return View("~/Views/StaffOps/ExecutiveReview/DecisionDetail.cshtml", model);
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        if (!decisions.Enabled) return NotFound();
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ReviewNotFoundException) { return NotFound(); }
        catch (ReviewValidationException error) { return BadRequest(localizer[error.Message].Value); }
    }
}
