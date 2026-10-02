using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The search box in the top bar: programmes, customers and work items for
/// anyone who can see the portfolio, plus people for those who manage staff.
/// </summary>
[Route("staffops/search")]
[RequireCapability(Capability.ViewPortfolio)]
public sealed class StaffSearchController(
    IPortfolioSearchService search,
    IStaffAuthorizationService authorization) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? q, CancellationToken cancellationToken = default)
    {
        var includePeople = await authorization.HasAsync(Capability.ManageStaff);
        var results = await search.SearchAsync(tenantId, q, includePeople, cancellationToken);

        ViewData["Title"] = "Search";
        return View("~/Views/StaffOps/Search.cshtml", results);
    }
}
