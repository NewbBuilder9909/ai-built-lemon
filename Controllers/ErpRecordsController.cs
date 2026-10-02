using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Resources;
using ProgrammePulse.Services.DemoData;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Controllers;

[Route("demo/erp-records")]
public sealed class ErpRecordsController(
    IDemoErpDataProvider dataProvider,
    IErpTransformationService transformationService,
    IDashboardAggregationService aggregationService,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("")]
    public IActionResult Index(string? q, string? status, string? validation, string? sort, string? dir)
    {
        var pipelineItems = transformationService.ProcessAll(dataProvider.GetRecords());
        var model = aggregationService.BuildRecordsList(pipelineItems, q, status, validation, sort, dir);

        ViewData["Title"] = localizer["ERP Records"];
        return View("~/Views/OperationsHub/ErpRecords.cshtml", model);
    }

    [HttpGet("{erpId}")]
    public IActionResult Detail(string erpId)
    {
        var pipelineItems = transformationService.ProcessAll(dataProvider.GetRecords());
        var model = aggregationService.BuildDetail(pipelineItems, erpId);

        if (model is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"{localizer["RecordTitlePrefix"]} {model.ErpId}";
        return View("~/Views/OperationsHub/ErpRecordDetail.cshtml", model);
    }
}
