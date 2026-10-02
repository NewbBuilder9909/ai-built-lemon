using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProgrammePulse.Services.Localization;

namespace ProgrammePulse.Controllers;

/// <summary>
/// Sets the culture cookie ASP.NET Core's built-in CookieRequestCultureProvider
/// reads on every subsequent request, then redirects back where the user was.
/// </summary>
[Route("language")]
public sealed class LanguageController(IOptions<LocalizationSettings>? options = null) : Controller
{
    private LocalizationSettings Localization => options?.Value ?? new LocalizationSettings();

    [HttpGet("")]
    public IActionResult Set(string culture, string? returnUrl, string? formatCulture = null)
    {
        var uiCultures = Localization.SupportedUiCultures;
        var formatCultures = Localization.SupportedFormatCultures;
        if (!uiCultures.Contains(culture)
            || (formatCulture is not null && !formatCultures.Contains(formatCulture)))
            return BadRequest();

        // Changing language must not silently change number/date formatting.
        var existing = CookieRequestCultureProvider.ParseCookieValue(Request.Cookies[CookieRequestCultureProvider.DefaultCookieName] ?? "");
        var previousFormat = existing?.Cultures.FirstOrDefault().ToString();
        var format = formatCulture ?? (formatCultures.Contains(previousFormat) ? previousFormat! : System.Globalization.CultureInfo.CurrentCulture.Name);
        if (!formatCultures.Contains(format)) format = Localization.DefaultCulture;
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(format, culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });

        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : Redirect("/");
    }
}
