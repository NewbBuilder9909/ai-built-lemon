using ProgrammePulse.Models.Staff;
using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Controllers;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Services.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Regression coverage for Programme Overview's split gate: viewing is Team
/// Lead or above, but running a sync is Admin-only (see the controller's own
/// doc comment). Negative-path only; see StaffAdminControllerTests for why.
///
/// Since the per-vendor sync actions collapsed into one Sync(source), the
/// entitlement check moved from a compile-time [RequireFeature] attribute to
/// a runtime call on the resolved source's FeatureKey. That check is no
/// longer covered by TenancyFilterTests, so the refusals it is responsible
/// for — unknown source, unresolved tenant, plan without the feature — are
/// asserted here instead, each one also asserting the source never ran.
/// </summary>
public class StaffProgrammeOverviewControllerTests
{
    private static StaffProgrammeOverviewController BuildSut(
        FakeStaffAuthorizationService auth,
        ISyncSourceRegistry? sources = null,
        IFeatureGate? featureGate = null,
        ITenantContext? tenantContext = null) =>
        new(null!, auth, null!, null!,
            new ProgrammeSyncService(
                sources ?? new FakeSyncSourceRegistry(),
                featureGate ?? new FakeFeatureGate(),
                null!, null!, TimeProvider.System, NullLogger<ProgrammeSyncService>.Instance),
            tenantContext ?? new FakeTenantContext(),
            null!, TimeProvider.System);

    private static FakeStaffAuthorizationService Admin() =>
        FakeStaffAuthorizationService.ForRole(StaffRole.Admin);

    [Fact]
    public async Task Index_forbids_callers_below_team_lead()
    {
        var result = await ActionGate.RunAsync<StaffProgrammeOverviewController>("Index", FakeStaffAuthorizationService.Nobody());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Sync_forbids_team_lead_who_is_not_admin()
    {
        // A Team Lead can view the overview but must not be able to trigger
        // a sync — this is the exact boundary a copy-paste of Index's gate
        // onto Sync would silently break.
        //
        // The gate is declared once on the parameterised Sync action and runs
        // before the source is even looked up, so it covers every registered
        // source by construction; there is no per-source path to check.
        var result = await ActionGate.RunAsync<StaffProgrammeOverviewController>(
            "Sync", FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Sync_admits_an_admin_to_the_body()
    {
        // The positive half: the source, tenant and plan checks below are
        // only reachable if the gate lets an Admin through.
        Assert.Null(await ActionGate.RunAsync<StaffProgrammeOverviewController>("Sync", Admin()));
    }

    [Fact]
    public async Task Sync_returns_not_found_for_an_unregistered_source()
    {
        var clickUp = FakeSyncSource.ClickUpLike();
        var sut = BuildSut(
            Admin(),
            new FakeSyncSourceRegistry(clickUp),
            new FakeFeatureGate(ProductFeature.ClickUpSync),
            FakeTenantContext.ResolvedOn(TenantPlan.Professional));

        Assert.IsType<NotFoundResult>(await sut.Sync("Jira"));
        Assert.Equal(0, clickUp.RunCount);
    }

    [Fact]
    public async Task Sync_forbids_when_no_tenant_can_be_resolved()
    {
        // Fails closed the same way the [RequireFeature] filter did: an
        // Admin whose StaffProfile has no usable tenant gets nothing, even
        // though the source exists and the plan check would have passed.
        var clickUp = FakeSyncSource.ClickUpLike();
        var sut = BuildSut(
            Admin(),
            new FakeSyncSourceRegistry(clickUp),
            new FakeFeatureGate(ProductFeature.ClickUpSync),
            new FakeTenantContext { IsResolved = false });

        Assert.IsType<ForbidResult>(await sut.Sync(clickUp.Name));
        Assert.Equal(0, clickUp.RunCount);
    }

    [Fact]
    public async Task Sync_forbids_a_source_the_tenants_plan_does_not_include()
    {
        // The entitlement that [RequireFeature(ProductFeature.HubPlannerSync)]
        // used to enforce: an Admin on a plan without the source is refused,
        // and refused before the source is asked to do anything.
        var hubPlanner = FakeSyncSource.HubPlannerLike();
        var sut = BuildSut(
            Admin(),
            new FakeSyncSourceRegistry(hubPlanner),
            new FakeFeatureGate(ProductFeature.ClickUpSync),
            FakeTenantContext.ResolvedOn(TenantPlan.Starter));

        Assert.IsType<ForbidResult>(await sut.Sync(hubPlanner.Name));
        Assert.Equal(0, hubPlanner.RunCount);
    }
}
