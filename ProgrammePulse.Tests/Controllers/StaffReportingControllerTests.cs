using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for the Reporting Hub's split gate: the hub itself
/// (including RAID and Governance) is Team Lead or above, but Cost Summary
/// and customer management are Admin-only (see the controller's own doc
/// comment — cost stays a separate route, structurally unreachable without
/// the check, not a flag on the main page). Negative-path only; the gate is
/// the declared [RequireCapability], run through ActionGate.
///
/// /staffops/reporting is served by five controllers since the Phase 1
/// split. Each case names the controller that now owns the action, so this
/// still covers every endpoint the single controller used to have.
/// RouteContractTests checks that the URLs and gates themselves did not move.
/// </summary>
public class StaffReportingControllerTests
{
    public static TheoryData<Type, string> HubActions => new()
    {
        { typeof(StaffReportingController), "Index" },
        { typeof(StaffReportingController), "Export" },
        { typeof(StaffReportingController), "Trend" },
        { typeof(StaffReportingController), "CaptureSnapshot" },
        { typeof(StaffReportingAlertsController), "Alerts" },
        { typeof(StaffReportingAlertsController), "AcknowledgeAlert" },
        { typeof(StaffReportingAlertsController), "RefreshAlerts" },
        { typeof(StaffRaidController), "Raid" },
        { typeof(StaffRaidController), "CreateRisk" },
        { typeof(StaffRaidController), "UpdateRiskStatus" },
        { typeof(StaffRaidController), "CreateIssue" },
        { typeof(StaffRaidController), "UpdateIssueStatus" },
        { typeof(StaffGovernanceController), "Governance" },
        { typeof(StaffGovernanceController), "LockBaseline" },
        { typeof(StaffGovernanceController), "CreateChangeRequest" },
        { typeof(StaffGovernanceController), "DecideChangeRequest" },
        { typeof(StaffGovernanceController), "AddStakeholder" },
        { typeof(StaffGovernanceController), "RemoveStakeholder" },
    };

    public static TheoryData<Type, string> AdminOnlyActions => new()
    {
        { typeof(StaffReportingController), "Cost" },
        { typeof(StaffReportingController), "ExportCost" },
        { typeof(StaffPortfolioAdminController), "SetProgrammeBudget" },
        { typeof(StaffCustomersController), "Index" },
        { typeof(StaffCustomersController), "Create" },
        { typeof(StaffCustomersController), "Assign" },
    };

    [Theory]
    [MemberData(nameof(HubActions))]
    [MemberData(nameof(AdminOnlyActions))]
    public async Task Forbids_callers_below_team_lead(Type controller, string action)
    {
        var result = await ActionGate.RunAsync(controller, action, FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyActions))]
    public async Task Forbids_team_lead_who_is_not_admin(Type controller, string action)
    {
        // A Team Lead can reach the main hub but must not reach cost/customer
        // management — the exact boundary a copy-paste of Index's gate would
        // silently break if applied here.
        //
        // Every hub capability is granted explicitly as well as by role, so
        // the refusal can only come from the Admin-only capability, and the
        // test keeps proving the Team Lead boundary specifically.
        var teamLead = FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead)
            .Granting(
                Capability.ViewDeliveryReporting, Capability.ViewProjectRisk, Capability.ViewPortfolio,
                Capability.ViewTeamCapacity, Capability.ManageProjectRisk, Capability.LockBaseline,
                Capability.DecideChangeRequest, Capability.CaptureTrend, Capability.ManageStakeholders);

        var result = await ActionGate.RunAsync(controller, action, teamLead);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData(typeof(StaffReportingController), "Index")]
    [InlineData(typeof(StaffRaidController), "Raid")]
    [InlineData(typeof(StaffGovernanceController), "Governance")]
    [InlineData(typeof(StaffReportingAlertsController), "Alerts")]
    public async Task Admits_a_team_lead_to_the_hub(Type controller, string action)
    {
        // The positive half of the boundary above: without it, a gate that
        // refused everyone would pass both refusal theories.
        var result = await ActionGate.RunAsync(controller, action, FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead));

        Assert.Null(result);
    }
}
