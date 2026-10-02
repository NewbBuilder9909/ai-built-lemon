using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Global MVC exception filter: a <see cref="CrossTenantReferenceException"/>
/// escaping an action becomes a 404, the same response the per-action
/// <c>catch (CrossTenantReferenceException) { return NotFound(); }</c>
/// blocks it replaces gave. A key from another tenant must be
/// indistinguishable from one that never existed, so every action gets that
/// answer by default instead of each one remembering to catch it.
///
/// Only this exception is handled. Area-specific validation exceptions
/// still need per-action handling, because each one re-renders its own form
/// with its own message.
/// </summary>
public sealed class CrossTenantReferenceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is CrossTenantReferenceException)
        {
            context.Result = new NotFoundResult();
            context.ExceptionHandled = true;
        }
    }
}
