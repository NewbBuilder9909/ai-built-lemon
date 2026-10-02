using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// The identity queue decides whose capacity a person's work counts toward,
/// so it is Admin-only on every action — including for a Team Lead who can
/// see the overview the queue is linked from. Negative-path only, same as
/// the other controller gate tests.
/// </summary>
public class StaffIdentityControllerTests
{
    [Theory]
    [InlineData("Index")]
    [InlineData("Link")]
    [InlineData("Approve")]
    [InlineData("Unlink")]
    public async Task Forbids_anonymous_callers(string action)
    {
        Assert.IsType<ForbidResult>(await ActionGate.RunAsync<StaffIdentityController>(action, new FakeStaffAuthorizationService()));
    }

    [Theory]
    [InlineData("Index")]
    [InlineData("Link")]
    [InlineData("Approve")]
    [InlineData("Unlink")]
    public async Task Forbids_a_team_lead_who_is_not_admin(string action)
    {
        var result = await ActionGate.RunAsync<StaffIdentityController>(action, FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead));

        Assert.IsType<ForbidResult>(result);
    }
}
