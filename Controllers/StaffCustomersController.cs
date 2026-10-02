using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Data → Customers: creating customers and assigning programmes to them,
/// so a review can be scoped to one customer. Customers are admin-authored,
/// not synced. Moved here from the Reporting Hub, which is a benched module.
/// </summary>
[Route("staffops/programme/customers")]
[RequireCapability(Capability.ManageCustomers)]
public sealed class StaffCustomersController(IPortfolioAdminService portfolioAdminService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, string? message = null)
    {
        var (customers, programmes) = await portfolioAdminService.GetFilterOptionsAsync(tenantId);

        ViewData["Title"] = "Customers";
        return View("~/Views/StaffOps/Programme/Customers.cshtml", new CustomersPageViewModel(customers, programmes, message));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([CurrentTenant] Guid tenantId, string? name)
    {
        var outcome = await portfolioAdminService.CreateCustomerAsync(tenantId, name);

        return RedirectToAction(nameof(Index), new { message = outcome.Error });
    }

    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign([CurrentTenant] Guid tenantId, Guid programmeKey, Guid? customerKey)
    {
        await portfolioAdminService.AssignProgrammeToCustomerAsync(tenantId, programmeKey, customerKey);

        return RedirectToAction(nameof(Index));
    }
}
