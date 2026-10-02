using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Services.DemoData;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Plain ASP.NET Core MVC controller — not tied to the Umbraco content tree.
/// Umbraco does not intercept user-defined routes, so this works alongside the
/// CMS's own routing with no extra registration beyond the DI wiring in
/// OperationsHubComposer.
/// </summary>
[Route("demo")]
public sealed class OperationsHubController(
    IDemoErpDataProvider dataProvider,
    IErpTransformationService transformationService,
    IDashboardAggregationService aggregationService) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        var pipelineItems = transformationService.ProcessAll(dataProvider.GetRecords());
        var model = aggregationService.BuildDashboard(pipelineItems);

        ViewData["Title"] = "Operations demo";
        return View("~/Views/OperationsHub/Dashboard.cshtml", model);
    }
}
