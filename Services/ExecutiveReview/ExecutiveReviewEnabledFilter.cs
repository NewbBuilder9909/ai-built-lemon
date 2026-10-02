using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ProgrammePulse.Services.ExecutiveReview;

/// <summary>
/// Makes a switched-off executive review look absent (404) to everyone,
/// before any plan or tenant check runs. A resource filter, so it executes
/// ahead of [RequireFeature]'s action filter — otherwise an anonymous or
/// tenant-less caller gets the plan gate's 403, which both breaks the
/// "disabled means not there" contract and advertises that the route exists.
/// The controllers' own Guard() check stays as defence in depth.
/// </summary>
public sealed class ExecutiveReviewEnabledAttribute() : TypeFilterAttribute(typeof(ExecutiveReviewEnabledFilter));

public sealed class ExecutiveReviewEnabledFilter(IOptions<ExecutiveReviewOptions> options) : IAsyncResourceFilter
{
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!options.Value.Enabled)
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        return next();
    }
}
