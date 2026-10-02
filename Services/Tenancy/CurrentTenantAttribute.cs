using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Binds an action's <c>Guid tenantId</c> parameter to the signed-in
/// member's resolved tenant (<see cref="ITenantContext"/>):
/// <code>public async Task&lt;IActionResult&gt; Index([CurrentTenant] Guid tenantId)</code>
///
/// Replaces the per-controller <c>ResolveTenantAsync()</c> helper and its
/// <c>if (tenantId is null) return Forbid();</c> guard. Two properties make
/// it safe to rely on:
///
/// - The binding source is not from the request, so a <c>?tenantId=</c>
///   query value, form field or route value is never read. The only way to
///   get a value is from ITenantContext.
/// - Declaring the parameter opts the action into <see cref="CurrentTenantFilter"/>
///   (registered globally), which refuses with 403 before the action runs
///   when no tenant is resolved. An action cannot receive the parameter
///   without the guard, and so cannot see <see cref="Guid.Empty"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CurrentTenantAttribute : ModelBinderAttribute
{
    public static readonly BindingSource Source = new(
        id: "CurrentTenant",
        displayName: "Current tenant",
        isGreedy: true,
        isFromRequest: false);

    public CurrentTenantAttribute() : base(typeof(CurrentTenantModelBinder))
    {
        BindingSource = Source;
    }
}

public sealed class CurrentTenantModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var tenantContext = bindingContext.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        await tenantContext.EnsureResolvedAsync();

        // Left unbound when unresolved: CurrentTenantFilter refuses the
        // request before the action can observe the default value.
        if (tenantContext.IsResolved && tenantContext.CurrentTenantId is { } tenantId)
        {
            bindingContext.Result = ModelBindingResult.Success(tenantId);
        }
    }
}

/// <summary>
/// Global action filter: any action with a <see cref="CurrentTenantAttribute"/>
/// parameter gets a 403 when the caller has no resolved tenant. Actions
/// without one are untouched. Runs after RequireCapabilityFilter, the same
/// order the in-body checks it replaces ran in (capability, then tenant),
/// though both refusals are the same ForbidResult.
/// </summary>
public sealed class CurrentTenantFilter(ITenantContext tenantContext) : IAsyncActionFilter
{
    public const int FilterOrder = 200;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!RequiresTenant(context.ActionDescriptor))
        {
            await next();
            return;
        }

        await tenantContext.EnsureResolvedAsync();
        if (!tenantContext.IsResolved || tenantContext.CurrentTenantId is null)
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }

    public static bool RequiresTenant(ActionDescriptor action) =>
        action.Parameters.Any(parameter => parameter.BindingInfo?.BindingSource == CurrentTenantAttribute.Source);
}
