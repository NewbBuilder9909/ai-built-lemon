using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for Contract Ops being admin-only on every single
/// action (see the controller's own doc comment — more sensitive than the
/// cost/rate data StaffAdminController gates, so nothing here is reachable
/// by Team Lead the way the Reporting Hub is). Negative-path only; the gate
/// is the declared [RequireCapability], run through ActionGate.
/// </summary>
public class StaffContractControllerTests
{
    public static TheoryData<string> Actions =>
    [
        "Index", "Overview", "Create", "Detail", "UpdateStatus", "UploadDocument", "DownloadDocument",
        "AddNonLabourCost", "GenerateInvoice", "InvoiceDocument", "UpdateInvoiceStatus"
    ];

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Forbids_non_admin_callers(string action)
    {
        var result = await ActionGate.RunAsync<StaffContractController>(action, FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Forbids_a_team_lead(string action)
    {
        var result = await ActionGate.RunAsync<StaffContractController>(
            action, FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead));

        Assert.IsType<ForbidResult>(result);
    }
}
