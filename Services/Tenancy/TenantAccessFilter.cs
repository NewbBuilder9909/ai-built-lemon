using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Global MVC filter (registered in TenancyComposer) that turns a Blocked
/// tenant (TenantAccessPolicy: suspended, archived, expired trial) into a
/// 403 for every /staffops request before the action runs — so a suspended
/// customer's members can't reach any page, tenant-scoped or not, without
/// every controller having to remember to check.
///
/// Exemptions: /staffops/account (they must still be able to log in, see the
/// message, and log out) and the anonymous theme stylesheet. Everything
/// outside /staffops (Umbraco backoffice, the ERP demo) is out of scope for
/// this filter and unaffected.
/// </summary>
public sealed class TenantAccessFilter(ITenantContext tenantContext) : IAsyncActionFilter
{
    public const string ViewPath = "~/Views/StaffOps/TenantSuspended.cshtml";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!AppliesTo(context.HttpContext.Request.Path))
        {
            await next();
            return;
        }

        await tenantContext.EnsureResolvedAsync();
        if (tenantContext.IsBlocked)
        {
            context.Result = new ViewResult
            {
                ViewName = ViewPath,
                StatusCode = StatusCodes.Status403Forbidden,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    ["Title"] = "Access suspended",
                    ["Reason"] = tenantContext.BlockedReason
                }
            };
            return;
        }

        await next();
    }

    public static bool AppliesTo(PathString path)
    {
        if (!path.StartsWithSegments("/staffops"))
        {
            return false;
        }

        if (path.StartsWithSegments("/staffops/account"))
        {
            return false;
        }

        if (path.StartsWithSegments("/staffops/branding/theme.css"))
        {
            return false;
        }

        return true;
    }
}
