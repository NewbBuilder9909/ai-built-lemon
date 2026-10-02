using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Declares that a controller or action belongs to a benched
/// <see cref="ProductModule"/>. While the tenant's Admin hasn't switched the
/// module on, every request answers 404 with a short page saying so. Sits
/// alongside [RequireFeature] (the plan) and [RequireCapability] (the role),
/// never instead of them.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireModuleAttribute : TypeFilterAttribute
{
    /// <summary>
    /// After [RequireCapability] (100), so someone the role refuses gets the
    /// usual refusal and learns nothing about which modules are on; before
    /// CurrentTenantFilter (200).
    /// </summary>
    public const int FilterOrder = 150;

    public RequireModuleAttribute(string module) : base(typeof(RequireModuleFilter))
    {
        Module = module;
        Arguments = [module];
        Order = FilterOrder;
    }

    public string Module { get; }
}

public sealed class RequireModuleFilter(string module, IModuleGate moduleGate) : IAsyncActionFilter
{
    public const string ViewPath = "~/Views/StaffOps/ModuleOff.cshtml";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await moduleGate.IsOnAsync(module))
        {
            context.Result = new ViewResult
            {
                ViewName = ViewPath,
                StatusCode = StatusCodes.Status404NotFound,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    ["Title"] = "Switched off",
                    ["Message"] = ProductModules.Find(module)?.Name ?? module
                }
            };
            return;
        }

        await next();
    }
}
