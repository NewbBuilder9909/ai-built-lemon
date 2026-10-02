using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using ProgrammePulse.Tests.Controllers;
using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Tenancy;

/// <summary>
/// The filters and binder that replaced the per-action
/// "HasAsync → Forbid; ResolveTenantAsync → Forbid" preamble and the
/// per-action CrossTenantReferenceException catch. ActionGate (used by the
/// controller tests) exercises these through real controller signatures;
/// this file pins down their individual behaviour, including the property
/// that matters most: a tenant can never be supplied by the request.
/// </summary>
public class DeclarativeGateFilterTests
{
    private sealed class Sample
    {
        public void Gated([CurrentTenant] Guid tenantId) { }

        public void Ungated(Guid tenantId) { }
    }

    private static (ActionExecutingContext Context, Func<bool> NextCalled, ActionExecutionDelegate Next) Arrange(string sampleAction)
    {
        var descriptor = ActionGate.Describe(typeof(Sample), typeof(Sample).GetMethod(sampleAction)!);
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        };
        return (context, () => nextCalled, next);
    }

    [Fact]
    public async Task RequireCapability_refuses_a_caller_without_it_and_never_runs_the_action()
    {
        var (context, nextCalled, next) = Arrange(nameof(Sample.Ungated));
        var filter = new RequireCapabilityFilter(Capability.ManageStaff, FakeStaffAuthorizationService.ForRole(StaffRole.TeamLead));

        await filter.OnActionExecutionAsync(context, next);

        Assert.IsType<ForbidResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task RequireCapability_admits_a_caller_who_holds_it()
    {
        var (context, nextCalled, next) = Arrange(nameof(Sample.Ungated));
        var filter = new RequireCapabilityFilter(Capability.ManageStaff, FakeStaffAuthorizationService.ForRole(StaffRole.Admin));

        await filter.OnActionExecutionAsync(context, next);

        Assert.Null(context.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public void Filters_run_in_the_order_the_in_body_checks_did()
    {
        // Feature (order 0) → capability → tenant. Changing this changes
        // which refusal a caller sees, e.g. "not in your plan" vs forbidden.
        Assert.Equal(0, new RequireFeatureAttribute("x").Order);
        Assert.Equal(RequireCapabilityAttribute.FilterOrder, new RequireCapabilityAttribute(Capability.ManageStaff).Order);
        Assert.True(RequireCapabilityAttribute.FilterOrder > 0);
        Assert.True(CurrentTenantFilter.FilterOrder > RequireCapabilityAttribute.FilterOrder);
    }

    [Fact]
    public async Task CurrentTenantFilter_refuses_an_action_that_needs_a_tenant_when_none_is_resolved()
    {
        var (context, nextCalled, next) = Arrange(nameof(Sample.Gated));

        await new CurrentTenantFilter(new FakeTenantContext { IsResolved = false }).OnActionExecutionAsync(context, next);

        Assert.IsType<ForbidResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task CurrentTenantFilter_refuses_a_blocked_tenant()
    {
        var (context, nextCalled, next) = Arrange(nameof(Sample.Gated));
        var blocked = new FakeTenantContext { IsResolved = false, IsBlocked = true, BlockedReason = "Suspended" };

        await new CurrentTenantFilter(blocked).OnActionExecutionAsync(context, next);

        Assert.IsType<ForbidResult>(context.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task CurrentTenantFilter_admits_a_resolved_tenant()
    {
        var (context, nextCalled, next) = Arrange(nameof(Sample.Gated));

        await new CurrentTenantFilter(FakeTenantContext.ResolvedOn("Enterprise")).OnActionExecutionAsync(context, next);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task CurrentTenantFilter_ignores_actions_that_do_not_ask_for_a_tenant()
    {
        // A plain Guid parameter named tenantId is not the attribute: the
        // filter keys on the binding source, never on a parameter name.
        var (context, nextCalled, next) = Arrange(nameof(Sample.Ungated));

        await new CurrentTenantFilter(new FakeTenantContext { IsResolved = false }).OnActionExecutionAsync(context, next);

        Assert.Null(context.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Binder_supplies_the_resolved_tenant_and_ignores_a_tenant_id_in_the_request()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var bindingContext = BindingContextFor(FakeTenantContext.ResolvedOn("Enterprise", mine), requestTenantId: theirs);

        await new CurrentTenantModelBinder().BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Equal(mine, bindingContext.Result.Model);
    }

    [Fact]
    public async Task Binder_leaves_the_parameter_unbound_when_no_tenant_is_resolved()
    {
        var bindingContext = BindingContextFor(new FakeTenantContext { IsResolved = false }, requestTenantId: Guid.NewGuid());

        await new CurrentTenantModelBinder().BindModelAsync(bindingContext);

        Assert.False(bindingContext.Result.IsModelSet);
    }

    [Fact]
    public void Cross_tenant_reference_becomes_not_found()
    {
        var context = ExceptionContextFor(new CrossTenantReferenceException("Project", Guid.NewGuid()));

        new CrossTenantReferenceExceptionFilter().OnException(context);

        Assert.IsType<NotFoundResult>(context.Result);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Other_exceptions_are_left_alone()
    {
        // Including the base type: an ordinary InvalidOperationException is
        // not evidence of a cross-tenant key and must not be disguised as 404.
        var context = ExceptionContextFor(new InvalidOperationException("boom"));

        new CrossTenantReferenceExceptionFilter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    private static DefaultModelBindingContext BindingContextFor(ITenantContext tenantContext, Guid requestTenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(tenantContext).BuildServiceProvider()
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var valueProvider = new QueryStringValueProvider(
            BindingSource.Query,
            new QueryCollection(new Dictionary<string, StringValues> { ["tenantId"] = requestTenantId.ToString() }),
            System.Globalization.CultureInfo.InvariantCulture);

        return (DefaultModelBindingContext)DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(Guid)),
            new BindingInfo { BindingSource = CurrentTenantAttribute.Source },
            modelName: "tenantId");
    }

    private static ExceptionContext ExceptionContextFor(Exception exception) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception
        };
}
