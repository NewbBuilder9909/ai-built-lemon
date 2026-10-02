using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Branding;
using ProgrammePulse.Services.BrandingOps;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for two things: Branding Ops being admin-only on
/// every mutating action, and the newer tenancy gate (see docs/tenancy.md)
/// — an Admin whose tenant can't be resolved must still be forbidden, not
/// silently treated as tenant-less. ThemeCss is the deliberate exception to
/// both: it's ungated by design (see the controller's own doc comment) and
/// must keep working for an anonymous/unresolved caller rather than
/// forbidding them.
/// </summary>
public class StaffBrandingControllerTests
{
    private static StaffBrandingController BuildSut(FakeTenantContext tenantContext, IBrandingThemeResolverService themeResolver) =>
        new(null!, tenantContext, null!, themeResolver);

    public static TheoryData<string> AdminActions =>
        ["Index", "SaveDraft", "Publish", "History", "Rollback", "UploadLogo", "UploadFavicon", "Preview"];

    [Theory]
    [MemberData(nameof(AdminActions))]
    public async Task Forbids_non_admin_callers(string action)
    {
        var result = await ActionGate.RunAsync<StaffBrandingController>(action, FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [MemberData(nameof(AdminActions))]
    public async Task Forbids_an_admin_whose_tenant_cannot_be_resolved(string action)
    {
        // Confirmed Admin, but Services/Tenancy/ITenantContext couldn't
        // resolve a tenant for them (see docs/tenancy.md) — every admin
        // action here needs both, not just the capability. Covers every
        // action now, not only Index: the upload actions used to reach the
        // tenant check through a private helper, which is where a gap would hide.
        var result = await ActionGate.RunAsync<StaffBrandingController>(
            action,
            FakeStaffAuthorizationService.ForRole(StaffRole.Admin),
            new FakeTenantContext { IsResolved = false });

        Assert.IsType<ForbidResult>(result);
    }

    /// <summary>
    /// The plan gate used to sit on the class, so every page's stylesheet
    /// link got the "Feature unavailable" HTML page back on the sign-in page
    /// and on any plan without Branding. ActionGate does not run the feature
    /// filter, so this checks the declarations themselves.
    /// </summary>
    [Fact]
    public void Only_the_admin_actions_are_plan_gated_so_the_stylesheet_always_loads()
    {
        static bool PlanGated(System.Reflection.MemberInfo member) =>
            member.GetCustomAttributes(typeof(ProgrammePulse.Services.Tenancy.RequireFeatureAttribute), inherit: true).Length > 0;

        Assert.False(PlanGated(typeof(StaffBrandingController)));
        Assert.False(PlanGated(typeof(StaffBrandingController).GetMethod(nameof(StaffBrandingController.ThemeCss))!));
        foreach (var action in new[] { "Index", "SaveDraft", "Publish", "History", "Rollback", "UploadLogo", "UploadFavicon", "Preview" })
            Assert.True(PlanGated(typeof(StaffBrandingController).GetMethod(action)!), $"{action} is not plan-gated");
    }

    [Fact]
    public async Task ThemeCss_never_forbids_an_anonymous_unresolved_caller()
    {
        // ThemeCss declares no gate at all: an anonymous caller reaches the body.
        Assert.Null(await ActionGate.RunAsync<StaffBrandingController>(
            "ThemeCss", new FakeStaffAuthorizationService(), new FakeTenantContext { IsResolved = false }));

        var themeResolver = new FakePlatformDefaultThemeResolverService();
        var sut = BuildSut(new FakeTenantContext { IsResolved = false }, themeResolver);
        // ThemeCss sets a response header directly, which needs an
        // HttpContext wired up — normally supplied by the MVC pipeline, not
        // present when a controller is constructed directly like this.
        sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await sut.ThemeCss();

        Assert.IsType<ContentResult>(result);
        Assert.Null(themeResolver.LastRequestedTenantId);
    }

    /// <summary>Only used by ThemeCss_never_forbids_an_anonymous_unresolved_caller — records what tenantId it was asked to resolve for.</summary>
    private sealed class FakePlatformDefaultThemeResolverService : IBrandingThemeResolverService
    {
        public Guid? LastRequestedTenantId { get; private set; }

        public Task<ResolvedBrandingTheme> GetActiveThemeAsync(Guid? tenantId)
        {
            LastRequestedTenantId = tenantId;
            return Task.FromResult(new ResolvedBrandingTheme
            {
                CssVariables = new Dictionary<string, string> { ["--brand-primary"] = PlatformDefaultTheme.PrimaryColour },
                HeaderStyle = PlatformDefaultTheme.HeaderStyle,
                FooterStyle = PlatformDefaultTheme.FooterStyle,
                DarkModeEnabled = PlatformDefaultTheme.DarkModeEnabled,
                CompanyName = PlatformDefaultTheme.CompanyName,
                IsUsingPlatformDefault = true
            });
        }

        public Task<ResolvedBrandingTheme> ResolvePreviewAsync(BrandingProfile profile) =>
            throw new NotSupportedException("Not exercised by this test.");

        public void InvalidateCache(Guid tenantId) =>
            throw new NotSupportedException("Not exercised by this test.");
    }
}
