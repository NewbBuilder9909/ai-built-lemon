using System.Security.Claims;

namespace ProgrammePulse.Services.Security;

/// <summary>Stable, structured event schema. Never accepts passwords, codes, tokens, request bodies or query strings.</summary>
public static class SecurityEvents
{
    public static void Write(ILogger logger, HttpContext? context, string eventName,
        string? memberId = null, Guid? tenantId = null, string? provider = null, bool warning = true)
    {
        logger.Log(warning ? LogLevel.Warning : LogLevel.Information, new EventId(4100, eventName),
            "Security event {SecurityEvent} MemberId={MemberId} TenantId={TenantId} SourceIp={SourceIp} Path={Path} CorrelationId={CorrelationId} Provider={Provider}",
            eventName, memberId ?? context?.User.FindFirstValue(ClaimTypes.NameIdentifier), tenantId,
            context?.Connection.RemoteIpAddress?.ToString(), context?.Request.Path.Value,
            context?.TraceIdentifier ?? System.Diagnostics.Activity.Current?.TraceId.ToString(), provider);
    }
}
