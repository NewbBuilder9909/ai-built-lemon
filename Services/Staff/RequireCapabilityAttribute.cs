using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Declares the <see cref="Models.Staff.Capability"/> a controller or action
/// requires, so who can reach an endpoint is readable from its signature
/// rather than from the first lines of its body. Several attributes (on the
/// class, the action, or both) are cumulative: every one must be held.
///
/// Replaces the in-body
/// <c>if (!await staffAuthorizationService.HasAsync(Capability.X)) return Forbid();</c>
/// guard with the same outcome at the same point in the pipeline. It is an
/// action filter, not an authorization filter, on purpose: the in-body check
/// ran after model binding, antiforgery validation, the global
/// TenantAccessFilter and any [RequireFeature], and changing that order
/// would change which refusal a caller sees (400 vs 403, "not in your plan"
/// vs forbidden). <see cref="FilterOrder"/> keeps it after all of those.
///
/// Mixed decisions that pick a *view* rather than refuse ("an Admin also
/// sees cost") stay as HasAsync calls in the body — this attribute is only
/// for the refuse-or-proceed gate.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireCapabilityAttribute : TypeFilterAttribute
{
    /// <summary>
    /// After RequireFeatureFilter and TenantAccessFilter (both order 0), and
    /// before CurrentTenantFilter (<see cref="Tenancy.CurrentTenantFilter.FilterOrder"/>),
    /// matching the order the in-body checks ran in.
    /// </summary>
    public const int FilterOrder = 100;

    public RequireCapabilityAttribute(string capability) : base(typeof(RequireCapabilityFilter))
    {
        Capability = capability;
        Arguments = [capability];
        Order = FilterOrder;
    }

    public string Capability { get; }
}

public sealed class RequireCapabilityFilter(string capability, IStaffAuthorizationService staffAuthorizationService) : IAsyncActionFilter
{
    public string Capability { get; } = capability;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await staffAuthorizationService.HasAsync(Capability))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
