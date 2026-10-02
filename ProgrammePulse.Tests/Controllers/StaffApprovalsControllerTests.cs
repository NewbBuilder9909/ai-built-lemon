using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for the Holiday Approver-or-Admin gate — the one
/// role tier not exercised by the other controller test files. Negative-path
/// only; the gate is the declared [RequireCapability], run through ActionGate.
/// </summary>
public class StaffApprovalsControllerTests
{
    [Theory]
    [InlineData("Index")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public async Task Forbids_callers_who_are_neither_holiday_approver_nor_admin(string action)
    {
        var result = await ActionGate.RunAsync<StaffApprovalsController>(action, FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("Index")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public async Task Admits_a_holiday_approver(string action)
    {
        var result = await ActionGate.RunAsync<StaffApprovalsController>(
            action, FakeStaffAuthorizationService.ForRole(StaffRole.HolidayApprover));

        Assert.Null(result);
    }
}
