using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Middleware;

public sealed class SecurityEventMiddleware(RequestDelegate next, ILogger<SecurityEventMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        var deniedRedirect = context.Response.StatusCode == 302
            && Uri.TryCreate(context.Response.Headers.Location.ToString(), UriKind.RelativeOrAbsolute, out var location)
            && (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0])
                .Equals("/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);
        if (context.Response.StatusCode is 401 or 403 || deniedRedirect)
            SecurityEvents.Write(logger, context, "AuthorizationDenied");
        else if (context.Response.StatusCode == 429)
            SecurityEvents.Write(logger, context, "RateLimitRejected");
    }
}
