using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Delivery load (Gold). Shows where someone's concurrent project count has
/// risen against their own recent history, so a delivery manager can
/// rebalance before it becomes a problem.
///
/// Two capabilities, following StaffEstimateCalibrationController: everyone
/// with delivery read sees the distribution as counts, and only a role that
/// can actually reassign work sees who. The named half is not a section this
/// controller hides in the view — the query service is told not to fetch
/// names at all, exactly as the calibration page treats estimator history
/// and as CLAUDE.md requires for cost data.
///
/// There is deliberately no export action and no sort parameter. An export
/// becomes a spreadsheet that outlives its caveats, and a sort parameter is
/// how an "elevated" list becomes an ordered one with a tail. Both would
/// undo the reason this page is allowed to exist; see docs/delivery-load.md.
/// </summary>
[Route("staffops/programme/load")]
[RequireModule(ProductModules.DeliveryLoad)]
public sealed class StaffDeliveryLoadController(
    IStaffAuthorizationService authorization,
    IDeliveryLoadQueryService deliveryLoad) : Controller
{
    [HttpGet("")]
    [RequireCapability(Capability.ViewTeamLoad)]
    public async Task<IActionResult> Index([CurrentTenant] Guid tenantId, CancellationToken cancellationToken = default)
    {
        var includePeople = await authorization.HasAsync(Capability.ViewPersonLoad);
        var model = await deliveryLoad.BuildAsync(tenantId, includePeople, cancellationToken);

        ViewData["Title"] = "Delivery load";
        return View("~/Views/StaffOps/Programme/Load.cshtml", model);
    }
}
