using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Declares that a controller or action is only available when the current
/// tenant's plan includes the given <see cref="Models.Tenancy.ProductFeature"/>.
/// Runs before the action; a tenant without the feature gets a 403 page
/// explaining it isn't in their plan. Sits alongside — never instead of —
/// the in-body HasAsync(Capability.*) capability checks.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireFeatureAttribute : TypeFilterAttribute
{
    public RequireFeatureAttribute(string feature) : base(typeof(RequireFeatureFilter))
    {
        Feature = feature;
        Arguments = [feature];
    }

    public string Feature { get; }
}

public sealed class RequireFeatureFilter(string feature, IFeatureGate featureGate) : IAsyncActionFilter
{
    public const string ViewPath = "~/Views/StaffOps/FeatureUnavailable.cshtml";

    public string Feature { get; } = feature;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var decision = await featureGate.EvaluateAsync(Feature);
        if (!decision.Enabled)
        {
            // The two refusals get different messages on purpose: "not in
            // your plan" is a commercial conversation, "no tenant resolved"
            // is an operator fix. Conflating them sends the customer to the
            // wrong person.
            context.Result = new ViewResult
            {
                ViewName = ViewPath,
                StatusCode = StatusCodes.Status403Forbidden,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    ["Title"] = decision.Outcome == FeatureGateOutcome.TenantUnresolved ? "Feature unavailable" : "Not included in your plan",
                    ["Feature"] = Feature,
                    ["Outcome"] = decision.Outcome.ToString(),
                    ["Plan"] = decision.Plan
                }
            };
            return;
        }

        await next();
    }
}
