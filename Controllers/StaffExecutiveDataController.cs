using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Models.Tenancy;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Executive data lifecycle: redaction, withdrawal, retention and tenant purge.
/// Every action needs ManageStaff, declared here so the gate runs before the
/// action does. ExecutiveDataService checks it again, and adds ManagePlatform
/// for a tenant purge.
/// </summary>
[Route("staffops/executive/data")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequireCapability(Capability.ManageStaff)]
[RequireModule(ProductModules.ExecutiveReview)]
public sealed class StaffExecutiveDataController(ExecutiveDataService data, IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("")]
    public Task<IActionResult> Index(ExecutiveDataRequest request) => Guard(async () => Page((await data.GetPageAsync()) with { Input = request }));

    [HttpPost("preview"), ValidateAntiForgeryToken]
    public Task<IActionResult> Preview(ExecutiveDataRequest request) => Guard(async () =>
    {
        var page = (await data.GetPageAsync()) with { Input = request };
        if (!ModelState.IsValid) return Page(page with { ErrorKey = "Review.Data.InvalidRequest" }, 400);
        return Page(page with { Preview = await data.PreviewAsync(request) });
    });

    [HttpPost("apply"), ValidateAntiForgeryToken]
    public Task<IActionResult> Apply(ExecutiveDataRequest request, string confirmationToken, bool confirmed) => Guard(async () =>
    {
        await data.GetPageAsync();
        if (!ModelState.IsValid) return Page((await data.GetPageAsync()) with { ErrorKey = "Review.Data.InvalidRequest" }, 400);
        var receipt = await data.ApplyAsync(request, confirmationToken, confirmed);
        ModelState.Clear();
        return Page((await data.GetPageAsync()) with { Receipt = receipt });
    });

    private ViewResult Page(ExecutiveDataPage model, int status = 200)
    {
        ViewData["Title"] = localizer["Review.Data.Title"];
        Response.StatusCode = status;
        return View("~/Views/StaffOps/ExecutiveReview/Data.cshtml", model);
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ReviewNotFoundException) { return NotFound(); }
        catch (ReviewConflictException) { return Page((await data.GetPageAsync()) with { ErrorKey = "Review.Data.Conflict" }, 409); }
        catch (ReviewValidationException error) { return Page((await data.GetPageAsync()) with { ErrorKey = error.Message }, 400); }
    }
}
