using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

[Route("staffops/executive")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ExecutiveReviewEnabled]
[RequireFeature(ProductFeature.ReportingHub)]
[RequireModule(ProductModules.ExecutiveReview)]
public sealed class StaffExecutiveReviewController(
    ExecutiveReviewService reviews, IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("")]
    public Task<IActionResult> Index() => Guard(async () =>
    {
        ViewData["Title"] = localizer["Review.Title"];
        return View("~/Views/StaffOps/ExecutiveReview/Index.cshtml", await reviews.GetOverviewAsync());
    });

    [HttpGet("{packKey:guid}")]
    public Task<IActionResult> Pack(Guid packKey) => Guard(async () =>
    {
        var pack = await reviews.GetPackAsync(packKey);
        if (pack is null) return NotFound();
        ViewData["Title"] = localizer["Review.Pack"];
        return View("~/Views/StaffOps/ExecutiveReview/Pack.cshtml", pack);
    });

    [HttpGet("{packKey:guid}/download")]
    public Task<IActionResult> Download(Guid packKey) => Guard(async () =>
    {
        var pack = await reviews.GetPackAsync(packKey);
        return pack is null ? NotFound() : File(JsonSerializer.SerializeToUtf8Bytes(pack),
            "application/json", $"operational-pack-{pack.PackKey:N}.json");
    });

    [HttpPost("capture"), ValidateAntiForgeryToken]
    public Task<IActionResult> Capture(string source) => Guard(async () =>
    {
        try
        {
            var pack = await reviews.CaptureAsync(source);
            return RedirectToAction(nameof(Pack), new { packKey = pack.PackKey });
        }
        catch (ReviewValidationException error)
        {
            ViewData["Title"] = localizer["Review.Title"];
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("~/Views/StaffOps/ExecutiveReview/Index.cshtml",
                (await reviews.GetOverviewAsync()) with { ErrorKey = error.Message });
        }
    });

    [HttpGet("/staffops/market")]
    public Task<IActionResult> Market() => Guard(async () =>
    {
        var current = await reviews.GetMarketAsync();
        return MarketView(new MarketSettingsInput
        {
            ExpectedVersion = current.Version, MarketCode = current.Settings.MarketCode,
            UiCulture = current.Settings.UiCulture, FormatCulture = current.Settings.FormatCulture,
            ReportingCurrency = current.Settings.ReportingCurrency, TimeZoneId = current.Settings.TimeZoneId,
            FiscalYearStartMonth = current.Settings.FiscalYearStartMonth
        });
    });

    [HttpPost("/staffops/market"), ValidateAntiForgeryToken]
    public Task<IActionResult> SaveMarket(MarketSettingsInput input) => Guard(async () =>
    {
        await reviews.GetMarketAsync();
        if (!ModelState.IsValid) return MarketView(input, "Review.InvalidSettings", 400);
        try
        {
            await reviews.SaveMarketAsync(input.ToSettings(), input.ExpectedVersion!.Value);
            return RedirectToAction(nameof(Market));
        }
        catch (ReviewConflictException)
        {
            return MarketView(input, "Review.Conflict", 409);
        }
        catch (ReviewValidationException error)
        {
            return MarketView(input, error.Message, 400);
        }
    });

    private ViewResult MarketView(MarketSettingsInput input, string? error = null, int status = 200)
    {
        ViewData["Title"] = localizer["Review.MarketSettings"];
        ViewData["ErrorKey"] = error;
        Response.StatusCode = status;
        return View("~/Views/StaffOps/ExecutiveReview/Market.cshtml", input);
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        if (!reviews.Enabled) return NotFound();
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
