using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProgrammePulse.Controllers;

/// <summary>
/// The page UseExceptionHandler re-executes for an unhandled exception
/// outside Development (see Startup/ErrorPagePolicy). It shows the request's
/// correlation id, which is on every log event for that request, so a
/// person reporting a problem can quote something support can find.
///
/// Deliberately self-contained: no layout, branding or tenant lookup, since
/// any of those might be what just failed. The exception itself is logged by
/// the exception-handling middleware and never shown.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController : Controller
{
    [Route("error")]
    public IActionResult ServerError()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("~/Views/Errors/ServerError.cshtml", HttpContext.TraceIdentifier);
    }
}
