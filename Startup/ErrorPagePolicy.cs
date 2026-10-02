using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ProgrammePulse.Startup;

/// <summary>
/// Whether unhandled exceptions are turned into the friendly error page
/// (ErrorController) rather than a bare 500. On everywhere except
/// Development, where the developer exception page is more useful.
/// <c>ErrorHandling:UseErrorPage</c> overrides it either way, e.g. to
/// rehearse the production page locally.
///
/// Expected failures never get here. A cross-tenant key is already a 404
/// (CrossTenantReferenceExceptionFilter), and a validation problem is a
/// message on the page it came from. This is for the unexpected ones.
/// </summary>
public static class ErrorPagePolicy
{
    public const string SettingKey = "ErrorHandling:UseErrorPage";
    public const string Path = "/error";

    public static bool UseErrorPage(IConfiguration configuration, string environmentName) =>
        configuration.GetValue<bool?>(SettingKey) ?? !string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
}
