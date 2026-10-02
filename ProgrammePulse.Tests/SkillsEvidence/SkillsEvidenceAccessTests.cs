using System.Reflection;
using ProgrammePulse.Tests.Controllers;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Who may do what with skills data. The interesting boundary is not
/// "admin vs not" — it is between the three different reads: your own
/// record, someone else's record, and the aggregate with nobody in it.
///
/// These assert against Models/Staff/RoleCapabilities, which is the
/// application's definition of access (PersonaContractTests separately
/// checks the persona files agree with it, so a change here that widens
/// access fails in two places).
/// </summary>
public class SkillsEvidenceAccessTests
{
    private static readonly string[] SkillsCapabilities =
    [
        Capability.ViewOwnSkills,
        Capability.DeclareOwnSkills,
        Capability.ViewTeamSkillCoverage,
        Capability.ViewStaffSkillEvidence,
        Capability.ReviewStaffSkills,
        Capability.ManageSkillTaxonomy
    ];

    [Fact]
    public void Every_employee_can_see_and_correct_their_own_record()
    {
        // Including the read-only roles. A skills matrix people cannot
        // inspect or contest is the failure mode the module's privacy
        // section is written against.
        foreach (var role in new[] { StaffRole.Staff, StaffRole.Analyst, StaffRole.Board, StaffRole.HolidayApprover, StaffRole.TeamLead, StaffRole.Admin })
        {
            Assert.True(RoleCapabilities.Has([role], Capability.ViewOwnSkills), $"{role} cannot see their own skills.");
            Assert.True(RoleCapabilities.Has([role], Capability.DeclareOwnSkills), $"{role} cannot declare or challenge their own skills.");
        }
    }

    [Fact]
    public void An_ordinary_employee_cannot_see_anyone_elses_record_or_the_aggregate()
    {
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ViewStaffSkillEvidence));
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ReviewStaffSkills));
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ViewTeamSkillCoverage));
        Assert.False(RoleCapabilities.Has([StaffRole.Staff], Capability.ManageSkillTaxonomy));
    }

    [Fact]
    public void The_delivery_manager_reviews_skills_but_does_not_own_the_taxonomy()
    {
        Assert.True(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ViewStaffSkillEvidence));
        Assert.True(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ReviewStaffSkills));
        Assert.True(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ViewTeamSkillCoverage));

        // A taxonomy anyone can extend stops being comparable, and the
        // coverage view built on it stops meaning anything.
        Assert.False(RoleCapabilities.Has([StaffRole.TeamLead], Capability.ManageSkillTaxonomy));
    }

    [Fact]
    public void The_read_only_roles_get_no_skills_access_beyond_their_own()
    {
        // Decision of 20 September 2026: even the aggregate is derived from
        // employees' personal records. Widening this is a deliberate act,
        // not a default — so it is asserted, not assumed.
        foreach (var role in new[] { StaffRole.Analyst, StaffRole.Board })
        {
            Assert.False(RoleCapabilities.Has([role], Capability.ViewTeamSkillCoverage), $"{role} unexpectedly sees skills coverage.");
            Assert.False(RoleCapabilities.Has([role], Capability.ViewStaffSkillEvidence), $"{role} unexpectedly sees other people's skills.");
            Assert.False(RoleCapabilities.Has([role], Capability.ReviewStaffSkills), $"{role} unexpectedly reviews skills.");
        }
    }

    [Fact]
    public void A_leave_approver_does_not_inherit_skills_review()
    {
        // The same separation the capability model was built for: approving
        // leave is not a reason to read someone's competence record.
        Assert.False(RoleCapabilities.Has([StaffRole.HolidayApprover], Capability.ViewStaffSkillEvidence));
        Assert.False(RoleCapabilities.Has([StaffRole.HolidayApprover], Capability.ReviewStaffSkills));
        Assert.False(RoleCapabilities.Has([StaffRole.HolidayApprover], Capability.ViewTeamSkillCoverage));
    }

    [Fact]
    public void The_tenant_administrator_holds_all_six()
    {
        foreach (var capability in SkillsCapabilities)
        {
            Assert.True(RoleCapabilities.Has([StaffRole.Admin], capability), $"Admin lacks {capability}.");
        }
    }

    [Fact]
    public void The_platform_operator_holds_none_of_them()
    {
        // Platform administration never confers access to tenant business
        // data, and an employee's competence record is about as far inside
        // a tenant as data gets.
        foreach (var capability in SkillsCapabilities)
        {
            Assert.False(RoleCapabilities.Has([StaffRole.PlatformAdmin], capability), $"Platform Admin unexpectedly holds {capability}.");
        }
    }

    [Fact]
    public void Reading_and_writing_stayed_separate_capabilities()
    {
        // The rule Capability.cs states about itself: collapsing a
        // read/write pair reintroduces the defect the split was built to
        // remove. Here that means a role can exist that sees coverage
        // without deciding anything — Board could be given exactly that
        // later without also being made a reviewer.
        Assert.NotEqual(Capability.ViewOwnSkills, Capability.DeclareOwnSkills);
        Assert.NotEqual(Capability.ViewStaffSkillEvidence, Capability.ReviewStaffSkills);

        Assert.DoesNotContain(
            RoleCapabilities.RolesGranting(Capability.ViewTeamSkillCoverage),
            role => !RoleCapabilities.Has([role], Capability.ViewStaffSkillEvidence));
    }

    [Fact]
    public void Every_new_skills_capability_is_held_by_someone_and_withheld_from_someone()
    {
        foreach (var capability in SkillsCapabilities)
        {
            var granting = RoleCapabilities.RolesGranting(capability);
            Assert.NotEmpty(granting);
            Assert.True(granting.Count < StaffRole.Seeded.Count,
                $"{capability} is granted to every seeded role, which means it is not a boundary at all.");
        }
    }

    /// <summary>
    /// Authorization is only real if every action performs it. This walks
    /// the controller's public actions by reflection and runs each one's
    /// declared gate (ActionGate, the production filters) for a caller with
    /// no capabilities, rather than trusting that a new action remembered
    /// to check. Index is the one imperative gate: it redirects to login
    /// instead of refusing, so it cannot be a [RequireCapability].
    /// </summary>
    [Fact]
    public async Task Every_action_on_the_skills_controller_is_gated()
    {
        var actions = SkillsActions();
        Assert.NotEmpty(actions);

        foreach (var action in actions.Where(a => a.Name != nameof(StaffSkillsController.Index)))
        {
            var result = await ActionGate.RunAsync<StaffSkillsController>(action.Name, FakeStaffAuthorizationService.Nobody());
            Assert.True(result is ForbidResult, $"StaffSkillsController.{action.Name} reached its body with no capabilities.");
        }

        var index = File.ReadAllText(ControllerSourcePath());
        Assert.Contains("HasAsync(Capability.ViewOwnSkills)", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_state_changing_action_on_the_skills_controller_validates_its_antiforgery_token()
    {
        var posts = SkillsActions().Where(a => a.GetCustomAttributes<HttpPostAttribute>().Any()).ToArray();

        Assert.NotEmpty(posts);
        Assert.All(posts, post => Assert.True(
            post.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any(),
            $"StaffSkillsController.{post.Name} is a POST without [ValidateAntiForgeryToken]."));
    }

    /// <summary>
    /// Every action must reach a tenant before touching data: either it
    /// declares a [CurrentTenant] parameter (refused with 403 when
    /// unresolved, before the body runs), or it is a self-service action
    /// whose own body resolves the caller through tenantContext.ResolveSelfAsync. Checked
    /// per action, not as a file-wide count, so one action that skips it
    /// cannot hide behind another that calls it twice.
    /// </summary>
    [Fact]
    public void Every_action_on_the_skills_controller_resolves_a_tenant()
    {
        var source = File.ReadAllText(ControllerSourcePath());

        foreach (var action in SkillsActions())
        {
            if (action.GetParameters().Any(p => p.GetCustomAttribute<CurrentTenantAttribute>() is not null))
            {
                continue;
            }

            var start = source.IndexOf($"> {action.Name}(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Could not find StaffSkillsController.{action.Name} in source.");
            var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
            var body = source[start..end];

            Assert.True(body.Contains("ResolveSelfAsync(currentStaff)", StringComparison.Ordinal),
                $"StaffSkillsController.{action.Name} neither takes [CurrentTenant] nor calls ResolveSelfAsync — it would read across tenants.");
        }
    }

    private static MethodInfo[] SkillsActions() =>
        typeof(StaffSkillsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToArray();

    private static string ControllerSourcePath()
    {
        // Walk up from the test binaries to the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgrammePulse.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Controllers", "StaffSkillsController.cs");
    }
}
