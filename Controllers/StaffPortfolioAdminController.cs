using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Setting programme budgets from the Cost Summary (ViewCommercials, the
/// Admin-only cost capability), part of the benched Reporting hub. Customers
/// moved to Data → Customers (StaffCustomersController). The rules live in
/// IPortfolioAdminService.
/// </summary>
[Route("staffops/reporting")]
[RequireModule(ProductModules.Reporting)]
public sealed class StaffPortfolioAdminController(IPortfolioAdminService portfolioAdminService) : Controller
{
    [HttpPost("cost/programme-budget")]
    [ValidateAntiForgeryToken]
    [RequireCapability(Capability.ViewCommercials)]
    public async Task<IActionResult> SetProgrammeBudget([CurrentTenant] Guid tenantId, Guid programmeKey, decimal? budgetAmount, string? budgetCurrency)
    {
        await portfolioAdminService.SetProgrammeBudgetAsync(tenantId, programmeKey, budgetAmount, budgetCurrency);

        return RedirectToAction(nameof(StaffReportingController.Cost), "StaffReporting");
    }
}
