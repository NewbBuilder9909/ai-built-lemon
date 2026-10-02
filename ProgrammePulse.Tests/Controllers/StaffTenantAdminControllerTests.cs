using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// The platform console is gated on Platform Admin, not Admin — the whole
/// point of the role split is that a customer's own Admin can't change their
/// plan or lift a suspension. Negative-path only, same as the other
/// controller gate tests; the pure ValidateNew seam is covered directly.
/// </summary>
public class StaffTenantAdminControllerTests
{
    [Theory]
    [InlineData("Index")]
    [InlineData("Create")]
    [InlineData("SetStatus")]
    [InlineData("SetPlan")]
    [InlineData("SetModules")]
    [InlineData("PurgePreview")]
    [InlineData("Purge")]
    public async Task Forbids_anonymous_callers(string action)
    {
        Assert.IsType<ForbidResult>(await ActionGate.RunAsync<StaffTenantAdminController>(action, new FakeStaffAuthorizationService()));
    }

    [Theory]
    [InlineData("Index")]
    [InlineData("Create")]
    [InlineData("SetStatus")]
    [InlineData("SetPlan")]
    [InlineData("SetModules")]
    [InlineData("PurgePreview")]
    [InlineData("Purge")]
    public async Task Forbids_a_tenant_admin_who_is_not_a_platform_admin(string action)
    {
        var result = await ActionGate.RunAsync<StaffTenantAdminController>(action, FakeStaffAuthorizationService.ForRole(StaffRole.Admin));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void ValidateNew_accepts_a_well_formed_tenant()
    {
        var errors = ProgrammePulse.Services.Tenancy.TenantAdminService.ValidateNew("Acme Ltd", "acme", TenantPlan.Professional, TenantStatus.Trial, ["default"]);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateNew_normalises_short_code_casing_rather_than_rejecting_it()
    {
        var errors = ProgrammePulse.Services.Tenancy.TenantAdminService.ValidateNew("Acme", "ACME", TenantPlan.Starter, TenantStatus.Active, []);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Has Spaces")]
    [InlineData("under_score")]
    [InlineData("way-too-long-for-a-short-code-value-x")]
    public void ValidateNew_rejects_a_malformed_short_code(string shortCode)
    {
        var errors = ProgrammePulse.Services.Tenancy.TenantAdminService.ValidateNew("Acme", shortCode, TenantPlan.Starter, TenantStatus.Active, []);

        Assert.Contains(errors, e => e.Contains("Short code"));
    }

    [Fact]
    public void ValidateNew_rejects_a_duplicate_short_code_case_insensitively()
    {
        var errors = ProgrammePulse.Services.Tenancy.TenantAdminService.ValidateNew("Acme", "ACME", TenantPlan.Starter, TenantStatus.Active, ["acme"]);

        Assert.Contains(errors, e => e.Contains("already in use"));
    }

    [Fact]
    public void ValidateNew_rejects_an_unknown_plan_and_a_non_usable_starting_status()
    {
        var errors = ProgrammePulse.Services.Tenancy.TenantAdminService.ValidateNew("Acme", "acme", "Platinum", TenantStatus.Archived, []);

        Assert.Contains(errors, e => e.Contains("not a known plan"));
        Assert.Contains(errors, e => e.Contains("Trial or Active"));
    }
}
