using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Tests.Staff;

/// <summary>
/// Pins the capability matrix against the legacy IStaffAuthorizationService
/// predicates it will replace.
///
/// The migration is deliberately additive: RoleCapabilities is live, the
/// Is…Async predicates still exist, and controllers move over one at a time.
/// That only stays safe while the two definitions are known to agree, so
/// these tests restate each legacy predicate as the group union it actually
/// uses, compare it to the matrix, and require every difference to be one of
/// the three decisions of 19 September 2026 — named individually below.
///
/// An unintended divergence therefore fails the build, which is the point:
/// the risk of running two access models side by side is that they drift.
/// </summary>
public class RoleCapabilitiesTests
{
    // The group unions the legacy predicates are built from, copied here on
    // purpose. If someone edits StaffAuthorizationService, these stop
    // matching and the tests below say so, rather than the suite silently
    // continuing to assert against a definition that has moved.
    private static readonly string[] LegacyIsStaff = [.. StaffRole.All];
    private static readonly string[] LegacyIsAdmin = [StaffRole.Admin];
    private static readonly string[] LegacyIsBoardOrAdmin = [StaffRole.Board, StaffRole.Admin];
    private static readonly string[] LegacyIsHolidayApproverOrAdmin = [StaffRole.HolidayApprover, StaffRole.Admin];
    private static readonly string[] LegacyIsTeamLeadOrAbove = [StaffRole.TeamLead, StaffRole.HolidayApprover, StaffRole.Admin];
    private static readonly string[] LegacyIsPlatformAdmin = [StaffRole.PlatformAdmin];

    private static IReadOnlyList<string> Granting(string capability) => RoleCapabilities.RolesGranting(capability);

    [Fact]
    public void Every_seeded_group_has_an_entry_so_no_role_falls_through_to_nothing_by_accident()
    {
        foreach (var role in StaffRole.Seeded)
        {
            Assert.True(RoleCapabilities.Map.ContainsKey(role), $"'{role}' has no capability entry.");
        }
    }

    [Fact]
    public void An_unknown_group_grants_nothing()
    {
        Assert.Empty(RoleCapabilities.For("Not A Real Group"));
        Assert.Empty(RoleCapabilities.For(null));
        Assert.Empty(RoleCapabilities.ForRoles(null));
    }

    [Fact]
    public void Every_capability_is_held_by_at_least_one_role()
    {
        var orphans = Capability.All.Where(c => Granting(c).Count == 0).ToArray();

        Assert.True(orphans.Length == 0,
            $"No role grants: {string.Join(", ", orphans)}. A capability nobody holds is dead code or a missing decision.");
    }

    [Fact]
    public void Holding_two_groups_adds_capabilities_rather_than_removing_them()
    {
        var both = RoleCapabilities.ForRoles([StaffRole.Staff, StaffRole.TeamLead]);

        Assert.Superset(RoleCapabilities.For(StaffRole.Staff).ToHashSet(), both.ToHashSet());
        Assert.Superset(RoleCapabilities.For(StaffRole.TeamLead).ToHashSet(), both.ToHashSet());
    }

    // ---- Unchanged from the legacy predicates ----

    [Fact]
    public void Self_service_matches_legacy_IsStaffAsync()
    {
        // StaffRole.All gained Analyst, so this also asserts the new group
        // really is an ordinary employee for self-service purposes.
        foreach (var capability in new[] { Capability.ViewOwnProfile, Capability.ViewOwnWork, Capability.SubmitOwnLeave })
        {
            Assert.Equal(LegacyIsStaff.Order(), Granting(capability).Order());
        }
    }

    [Fact]
    public void Commercial_and_administrative_capabilities_stay_admin_only()
    {
        foreach (var capability in new[]
                 {
                     Capability.ViewCommercials, Capability.ManageContracts, Capability.ManageStaff,
                     Capability.ViewAudit, Capability.ManageIntegrations, Capability.ManageIdentityMappings,
                     Capability.ManageBranding, Capability.ManageCustomers, Capability.ViewEstimatorHistory,
                     Capability.ManageEstimateCalibration
                 })
        {
            Assert.Equal(LegacyIsAdmin.Order(), Granting(capability).Order());
        }
    }

    [Fact]
    public void Platform_management_stays_platform_admin_only_and_grants_nothing_else()
    {
        Assert.Equal(LegacyIsPlatformAdmin.Order(), Granting(Capability.ManagePlatform).Order());

        // The section 3 determination, as a property of the matrix itself:
        // the platform operator's entire capability set is that one entry.
        Assert.Equal([Capability.ManagePlatform], RoleCapabilities.For(StaffRole.PlatformAdmin));
    }

    [Fact]
    public void Estimate_calibration_aggregates_stay_with_board_and_admin()
    {
        Assert.Equal(LegacyIsBoardOrAdmin.Order(), Granting(Capability.ViewEstimateCalibration).Order());
    }

    // ---- The three intended divergences ----

    /// <summary>
    /// Decision 2. Board previously held no delivery capability at all
    /// (defect D4): it is not in the IsTeamLeadOrAboveAsync union, so the
    /// portfolio, reporting hub, RAID and trend pages all refused it.
    /// </summary>
    [Fact]
    public void Decision_2_board_gains_delivery_read_but_never_delivery_write()
    {
        foreach (var capability in new[]
                 {
                     Capability.ViewPortfolio, Capability.ViewDeliveryReporting,
                     Capability.ViewProjectRisk, Capability.ViewTeamCapacity
                 })
        {
            Assert.DoesNotContain(StaffRole.Board, LegacyIsTeamLeadOrAbove);
            Assert.Contains(StaffRole.Board, Granting(capability));
        }

        foreach (var capability in new[]
                 {
                     Capability.ManageProjectRisk, Capability.LockBaseline, Capability.DecideChangeRequest,
                     Capability.CaptureTrend, Capability.ManageStakeholders, Capability.TriggerSync
                 })
        {
            Assert.DoesNotContain(StaffRole.Board, Granting(capability));
        }
    }

    /// <summary>
    /// Decision 3. Holiday Approver sat inside the IsTeamLeadOrAboveAsync
    /// union (defect D3), so a leave approver could lock baselines and decide
    /// change requests. Reporting read is kept — approving leave sensibly
    /// needs a capacity view — but every write is withdrawn.
    /// </summary>
    [Fact]
    public void Decision_3_holiday_approver_keeps_reporting_read_and_loses_every_delivery_write()
    {
        Assert.Contains(StaffRole.HolidayApprover, LegacyIsTeamLeadOrAbove);

        Assert.Contains(StaffRole.HolidayApprover, Granting(Capability.ViewDeliveryReporting));
        Assert.Contains(StaffRole.HolidayApprover, Granting(Capability.ViewTeamCapacity));

        foreach (var capability in new[]
                 {
                     Capability.ManageProjectRisk, Capability.LockBaseline, Capability.DecideChangeRequest,
                     Capability.CaptureTrend, Capability.ManageStakeholders, Capability.TriggerSync
                 })
        {
            Assert.DoesNotContain(StaffRole.HolidayApprover, Granting(capability));
        }
    }

    /// <summary>
    /// Decision 4. ApproveLeave was Holiday Approver and Admin only, so a
    /// Team Lead could not approve their own team's leave.
    /// </summary>
    [Fact]
    public void Decision_4_team_lead_gains_approve_leave()
    {
        Assert.DoesNotContain(StaffRole.TeamLead, LegacyIsHolidayApproverOrAdmin);

        Assert.Equal(
            new[] { StaffRole.Admin, StaffRole.HolidayApprover, StaffRole.TeamLead }.Order(),
            Granting(Capability.ApproveLeave).Order());
    }

    /// <summary>
    /// Decision 1. The Analyst group exists so an analyst is not made a Team
    /// Lead purely to read a report — so the test that matters is that it
    /// carries delivery read and no write, no commercials and no admin.
    /// </summary>
    [Fact]
    public void Decision_1_analyst_is_read_only()
    {
        var analyst = RoleCapabilities.For(StaffRole.Analyst);

        Assert.Contains(Capability.ViewPortfolio, analyst);
        Assert.Contains(Capability.ViewDeliveryReporting, analyst);
        Assert.Contains(Capability.ViewProjectRisk, analyst);
        Assert.Contains(Capability.ViewTeamCapacity, analyst);

        foreach (var forbidden in new[]
                 {
                     Capability.ManageProjectRisk, Capability.LockBaseline, Capability.DecideChangeRequest,
                     Capability.CaptureTrend, Capability.ManageStakeholders, Capability.TriggerSync,
                     Capability.ViewCommercials, Capability.ManageContracts, Capability.ManageStaff,
                     Capability.ViewAudit, Capability.ManageIntegrations, Capability.ManageBranding,
                     Capability.ApproveLeave, Capability.ManagePlatform
                 })
        {
            Assert.DoesNotContain(forbidden, analyst);
        }
    }

    /// <summary>
    /// The read/write split is the reason this model exists (defect D2), so
    /// assert it as a property rather than trusting each role's entry: no
    /// role may hold a delivery write capability without the matching read.
    /// </summary>
    [Fact]
    public void No_role_can_write_delivery_data_it_cannot_read()
    {
        // TriggerSync is deliberately absent: it moved to tenant
        // administration once the persona contracts showed the application
        // had always treated pulling from an external source as an
        // integration action rather than a delivery edit.
        string[] writes =
        [
            Capability.ManageProjectRisk, Capability.LockBaseline, Capability.DecideChangeRequest,
            Capability.CaptureTrend, Capability.ManageStakeholders
        ];

        foreach (var (role, capabilities) in RoleCapabilities.Map)
        {
            if (writes.Any(capabilities.Contains))
            {
                Assert.True(capabilities.Contains(Capability.ViewDeliveryReporting),
                    $"'{role}' can write delivery data but cannot read it.");
            }
        }
    }
}
