using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ProgrammePulse.Resources;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Where a refused member lands. Umbraco's member cookie scheme turns every
/// <c>Forbid()</c> into a 302 to <c>/Account/AccessDenied</c> — an ASP.NET
/// Core Identity default this application never defined, so the request fell
/// through to Umbraco's "Welcome to your Umbraco installation" page.
///
/// The path is kept, not changed: <c>SecurityEventMiddleware</c> and the
/// persona harness (<c>PersonaSession.AccessDeniedPath</c>) both recognise a
/// denial by the redirect pointing here. Returns 200 rather than 403 because
/// the refusal was already recorded on the redirect; a 403 here would log a
/// second AuthorizationDenied event for the same decision.
/// </summary>
[Route("Account/AccessDenied")]
public sealed class AccessDeniedController(IStringLocalizer<SharedResource> localizer) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = localizer["AccessDenied.PageTitle"];
        return View("~/Views/StaffOps/AccessDenied.cshtml");
    }
}
