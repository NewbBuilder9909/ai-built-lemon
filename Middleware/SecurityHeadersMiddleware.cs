using Microsoft.AspNetCore.Http;

namespace ProgrammePulse.Middleware;

/// <summary>
/// Headers that are safe on every response, including the Umbraco backoffice
/// and its content-preview iframe (X-Frame-Options is SAMEORIGIN, not DENY,
/// for exactly that reason). The Content-Security-Policy is scoped to this
/// app's own custom UI (/staffops, /demo, /language) only — the backoffice is a
/// complex SPA this session can't fully verify against a CSP, so it's left
/// alone rather than risk breaking it.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string CspPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; form-action 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; script-src 'self'; frame-ancestors 'self';";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            // Apply compatible navigation/embed restrictions across the backoffice too.
            headers["Content-Security-Policy"] = "base-uri 'self'; object-src 'none'; frame-ancestors 'self';";

            var path = context.Request.Path;
            if (path.StartsWithSegments("/staffops") || path.StartsWithSegments("/demo") || path.StartsWithSegments("/language"))
            {
                headers["Content-Security-Policy"] = CspPolicy;
            }

            if (path.StartsWithSegments("/staffops/account"))
                headers["Cache-Control"] = "no-store";

            return Task.CompletedTask;
        });

        await next(context);
    }
}
