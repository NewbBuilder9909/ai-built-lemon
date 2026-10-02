using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Tests.Controllers;

/// <summary>
/// Runs an action's declared gate, the <see cref="RequireCapabilityAttribute"/>s
/// on its controller and method and the <see cref="CurrentTenantFilter"/> that
/// a <see cref="CurrentTenantAttribute"/> parameter opts it into, using the
/// production filter classes against a descriptor built from the real method.
///
/// Returns the short-circuit result (a ForbidResult) when the gate refuses,
/// or null when the request would reach the action body. The refusal tests
/// used to call the action directly and read the ForbidResult it returned;
/// the gate now lives in filters, so this runs those filters instead. The
/// question each test asks ("is a Team Lead refused Cost?") is unchanged.
///
/// <see cref="RequireFeatureAttribute"/> is not evaluated here: plan
/// entitlement is its own concern with its own tests.
/// </summary>
public static class ActionGate
{
    /// <param name="action">
    /// The method name. For an overloaded GET/POST pair, suffix "_Get" or
    /// "_Post" (e.g. "Create_Post") to pick one by its HTTP attribute.
    /// </param>
    public static Task<IActionResult?> RunAsync<TController>(
        string action, IStaffAuthorizationService authorization, ITenantContext? tenantContext = null)
        where TController : Controller =>
        RunAsync(typeof(TController), action, authorization, tenantContext);

    public static async Task<IActionResult?> RunAsync(
        Type controllerType, string action, IStaffAuthorizationService authorization, ITenantContext? tenantContext = null)
    {
        var method = FindAction(controllerType, action);
        var tenant = tenantContext ?? FakeTenantContext.ResolvedOn("Enterprise");

        var capabilityFilters = controllerType.GetCustomAttributes<RequireCapabilityAttribute>(inherit: true)
            .Concat(method.GetCustomAttributes<RequireCapabilityAttribute>(inherit: true))
            .Select(attribute => (IAsyncActionFilter)new RequireCapabilityFilter(attribute.Capability, authorization));

        // Same relative order as production: RequireCapabilityAttribute.FilterOrder
        // (100) before CurrentTenantFilter.FilterOrder (200).
        IAsyncActionFilter[] pipeline = [.. capabilityFilters, new CurrentTenantFilter(tenant)];

        var context = new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), Describe(controllerType, method)),
            [],
            new Dictionary<string, object?>(),
            controller: null!);

        foreach (var filter in pipeline)
        {
            var reachedNext = false;
            await filter.OnActionExecutionAsync(context, () =>
            {
                reachedNext = true;
                return Task.FromResult(new ActionExecutedContext(context, [], controller: null!));
            });

            if (!reachedNext)
            {
                return context.Result;
            }
        }

        return null;
    }

    public static MethodInfo FindAction(Type controller, string action)
    {
        var (name, verb) = action.EndsWith("_Get", StringComparison.Ordinal) ? (action[..^4], "GET")
            : action.EndsWith("_Post", StringComparison.Ordinal) ? (action[..^5], "POST")
            : (action, null);

        var candidates = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == name)
            .Where(m => verb is null || m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Contains(verb)))
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException($"{controller.Name}.{action} matched {candidates.Length} actions; use a _Get/_Post suffix.");
    }

    public static ControllerActionDescriptor Describe(Type controller, MethodInfo method) => new()
    {
        ControllerTypeInfo = controller.GetTypeInfo(),
        MethodInfo = method,
        ActionName = method.Name,
        Parameters = method.GetParameters()
            .Select(parameter => (ParameterDescriptor)new ControllerParameterDescriptor
            {
                Name = parameter.Name!,
                ParameterType = parameter.ParameterType,
                ParameterInfo = parameter,
                BindingInfo = BindingInfo.GetBindingInfo(parameter.GetCustomAttributes(inherit: true))
            })
            .ToList()
    };
}
