using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace ProgrammePulse.Middleware;

/// <summary>
/// Gives every request a correlation id that appears (a) on the response as
/// X-Correlation-ID, (b) as HttpContext.TraceIdentifier, and (c) as a
/// "CorrelationId" property on every Serilog event written while the
/// request is in flight — Umbraco's Serilog setup already enriches from
/// LogContext, so nothing else needs configuring for it to land in
/// umbraco/Logs/UmbracoTraceLog.*.json.
///
/// An inbound X-Correlation-ID is honoured (so an edge proxy or an upstream
/// caller can stitch its own trace together) but sanitised to a bounded set
/// of characters first — a header is attacker-controlled input and this
/// value ends up in log files.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].ToString());

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>Exposed for tests: accepts a well-formed inbound id, otherwise mints a new one.</summary>
    public static string ResolveCorrelationId(string? inbound)
    {
        if (string.IsNullOrWhiteSpace(inbound))
        {
            return Guid.NewGuid().ToString("N");
        }

        var trimmed = inbound.Trim();
        if (trimmed.Length > MaxLength || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
        {
            return Guid.NewGuid().ToString("N");
        }

        return trimmed;
    }
}
