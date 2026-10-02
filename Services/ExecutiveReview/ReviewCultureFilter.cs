using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.ExecutiveReview;

// Resource filters execute after authentication but before form model binding.
public sealed class ReviewCultureFilter(
    ITenantContext tenant, IMarketSettingsRepository settings,
    IOptions<ExecutiveReviewOptions> options,
    IOptions<ProgrammePulse.Services.Localization.LocalizationSettings> localization) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!options.Value.Enabled || !(request.Path.StartsWithSegments("/staffops/executive")
            || request.Path.StartsWithSegments("/staffops/market")))
        {
            await next();
            return;
        }
        await tenant.EnsureResolvedAsync();
        if (tenant.CurrentTenantId is not Guid tenantId)
        {
            await next();
            return;
        }
        var market = (await settings.GetAsync(tenantId)).Settings;
        var cookie = CookieRequestCultureProvider.ParseCookieValue(request.Cookies[CookieRequestCultureProvider.DefaultCookieName] ?? "");
        var ui = cookie?.UICultures.FirstOrDefault().ToString();
        var format = cookie?.Cultures.FirstOrDefault().ToString();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(localization.Value.SupportedFormatCultures.Contains(format) ? format! : market.FormatCulture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(localization.Value.SupportedUiCultures.Contains(ui) ? ui! : market.UiCulture);
            await next();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
