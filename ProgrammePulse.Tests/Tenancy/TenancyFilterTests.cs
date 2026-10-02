using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Tenancy;

/// <summary>
/// The two MVC filters that enforce tenancy outside controller bodies:
/// TenantAccessFilter (global; blocked tenant → 403 on every /staffops
/// action except login/logout and the theme stylesheet) and
/// RequireFeatureFilter ([RequireFeature]; plan without the feature → 403).
/// </summary>
public class TenancyFilterTests
{
    private sealed class FakeFeatureGate(bool enabled, FeatureGateOutcome? outcome = null) : IFeatureGate
    {
        public string? LastFeature { get; private set; }

        public Task<FeatureGateDecision> EvaluateAsync(string feature)
        {
            LastFeature = feature;
            return Task.FromResult(new FeatureGateDecision(enabled, outcome ?? (enabled ? FeatureGateOutcome.Enabled : FeatureGateOutcome.NotInPlan), TenantPlan.Starter));
        }

        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(enabled);
    }

    private static (ActionExecutingContext Context, Func<bool> NextCalled, ActionExecutionDelegate Next) Arrange(string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        };
        return (context, () => nextCalled, next);
    }

    [Theory]
    [InlineData("/staffops", true)]
    [InlineData("/staffops/admin", true)]
    [InlineData("/staffops/programme/sync", true)]
    [InlineData("/staffops/account/login", false)]
    [InlineData("/staffops/account/logout", false)]
    [InlineData("/staffops/branding/theme.css", false)]
    [InlineData("/umbraco/management/api/v1/anything", false)]
    [InlineData("/demo", false)]
    public void TenantAccessFilter_applies_only_to_staffops_pages_that_are_not_login_or_theme(string path, bool expected) =>
        Assert.Equal(expected, TenantAccessFilter.AppliesTo(new PathString(path)));

    [Fact]
    public async Task TenantAccessFilter_short_circuits_a_blocked_tenant_with_403()
    {
        var (context, nextCalled, next) = Arrange("/staffops/reporting");
        var sut = new TenantAccessFilter(new FakeTenantContext { IsBlocked = true, BlockedReason = "suspended" });

        await sut.OnActionExecutionAsync(context, next);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, view.StatusCode);
        Assert.Equal(TenantAccessFilter.ViewPath, view.ViewName);
        Assert.Equal("suspended", view.ViewData["Reason"]);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task TenantAccessFilter_lets_a_blocked_tenant_reach_the_login_and_logout_routes()
    {
        var (context, nextCalled, next) = Arrange("/staffops/account/logout");
        var sut = new TenantAccessFilter(new FakeTenantContext { IsBlocked = true, BlockedReason = "suspended" });

        await sut.OnActionExecutionAsync(context, next);

        Assert.Null(context.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task TenantAccessFilter_passes_through_resolved_and_anonymous_callers()
    {
        foreach (var tenant in new[] { FakeTenantContext.ResolvedOn(TenantPlan.Starter), new FakeTenantContext() })
        {
            var (context, nextCalled, next) = Arrange("/staffops/admin");

            await new TenantAccessFilter(tenant).OnActionExecutionAsync(context, next);

            Assert.Null(context.Result);
            Assert.True(nextCalled());
        }
    }

    [Fact]
    public async Task RequireFeatureFilter_blocks_with_403_when_the_plan_lacks_the_feature()
    {
        var (context, nextCalled, next) = Arrange("/staffops/contracts");
        var gate = new FakeFeatureGate(enabled: false);
        var sut = new RequireFeatureFilter(ProductFeature.ContractOps, gate);

        await sut.OnActionExecutionAsync(context, next);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, view.StatusCode);
        Assert.Equal(RequireFeatureFilter.ViewPath, view.ViewName);
        Assert.Equal(ProductFeature.ContractOps, gate.LastFeature);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task RequireFeatureFilter_tells_an_unresolved_tenant_it_is_a_setup_problem_not_a_plan_limit()
    {
        var (context, nextCalled, next) = Arrange("/staffops/programme/sync");
        var sut = new RequireFeatureFilter(ProductFeature.ClickUpSync, new FakeFeatureGate(enabled: false, FeatureGateOutcome.TenantUnresolved));

        await sut.OnActionExecutionAsync(context, next);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, view.StatusCode);
        Assert.Equal("TenantUnresolved", view.ViewData["Outcome"]);
        Assert.Equal("Feature unavailable", view.ViewData["Title"]);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task RequireFeatureFilter_continues_when_the_feature_is_enabled()
    {
        var (context, nextCalled, next) = Arrange("/staffops/contracts");
        var sut = new RequireFeatureFilter(ProductFeature.ContractOps, new FakeFeatureGate(enabled: true));

        await sut.OnActionExecutionAsync(context, next);

        Assert.Null(context.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public void RequireFeature_attribute_passes_the_feature_key_to_the_filter()
    {
        var attribute = new RequireFeatureAttribute(ProductFeature.HubPlannerSync);

        Assert.Equal(typeof(RequireFeatureFilter), attribute.ImplementationType);
        Assert.Equal([ProductFeature.HubPlannerSync], attribute.Arguments);
    }
}
