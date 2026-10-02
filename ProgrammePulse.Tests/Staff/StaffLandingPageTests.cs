using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Tests.Staff;

/// <summary>
/// Pins where each role lands after sign-in, read through the real
/// <see cref="RoleCapabilities"/> matrix so a role change moves the landing
/// page with it rather than leaving this test describing a role that no
/// longer exists.
/// </summary>
public class StaffLandingPageTests
{
    [Theory]
    [InlineData(StaffRole.Admin, StaffLandingPage.Review)]
    [InlineData(StaffRole.TeamLead, StaffLandingPage.Review)]
    [InlineData(StaffRole.Board, StaffLandingPage.Review)]
    [InlineData(StaffRole.Analyst, StaffLandingPage.Review)]
    [InlineData(StaffRole.PlatformAdmin, StaffLandingPage.PlatformConsole)]
    public async Task Each_role_lands_on_the_page_it_gets_value_from(string role, string expected)
    {
        var landing = await StaffLandingPage.ResolveAsync(FakeStaffAuthorizationService.ForRole(role), selfServiceOn: false);

        Assert.Equal(expected, landing);
    }

    [Theory]
    [InlineData(true, StaffLandingPage.MyWork)]
    [InlineData(false, StaffLandingPage.Start)]
    public async Task Staff_land_on_their_own_work_only_while_self_service_is_switched_on(bool selfServiceOn, string expected)
    {
        var landing = await StaffLandingPage.ResolveAsync(FakeStaffAuthorizationService.ForRole(StaffRole.Staff), selfServiceOn);

        Assert.Equal(expected, landing);
    }

    [Fact]
    public async Task A_signed_out_or_deactivated_member_is_sent_to_login()
    {
        var landing = await StaffLandingPage.ResolveAsync(FakeStaffAuthorizationService.Nobody(), selfServiceOn: true);

        Assert.Equal(StaffLandingPage.Login, landing);
    }

    /// <summary>
    /// The resolver must never pick a page the member cannot open — that was
    /// the Platform Admin defect: MFA succeeded, then /staffops refused them
    /// and the refusal looked like being logged out. The start page needs no
    /// capability: it is what a signed-in member with nothing switched on sees.
    /// </summary>
    [Theory]
    [InlineData(StaffRole.Admin, false)]
    [InlineData(StaffRole.HolidayApprover, false)]
    [InlineData(StaffRole.TeamLead, false)]
    [InlineData(StaffRole.Staff, false)]
    [InlineData(StaffRole.Staff, true)]
    [InlineData(StaffRole.Board, false)]
    [InlineData(StaffRole.Analyst, false)]
    [InlineData(StaffRole.PlatformAdmin, false)]
    public async Task The_landing_page_is_one_the_role_can_open(string role, bool selfServiceOn)
    {
        var authorization = FakeStaffAuthorizationService.ForRole(role);
        var landing = await StaffLandingPage.ResolveAsync(authorization, selfServiceOn);

        var requiredCapability = landing switch
        {
            StaffLandingPage.Review => Capability.ViewDeliveryReporting,
            StaffLandingPage.ProgrammeOverview => Capability.ViewPortfolio,
            StaffLandingPage.MyWork => Capability.ViewOwnWork,
            StaffLandingPage.PlatformConsole => Capability.ManagePlatform,
            StaffLandingPage.Start => null,
            _ => throw new Xunit.Sdk.XunitException($"{role} landed on {landing}, which is not a signed-in page.")
        };

        Assert.True(requiredCapability is null || await authorization.HasAsync(requiredCapability),
            $"{role} lands on {landing} but lacks {requiredCapability}, so the page would refuse them.");
    }
}
