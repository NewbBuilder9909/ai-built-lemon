using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ProgrammePulse.Middleware;
using ProgrammePulse.Services.Security;

namespace ProgrammePulse.Tests.Security;

public class SecurityEventsTests
{
    [Fact]
    public void Event_contains_security_context_but_not_query_or_body()
    {
        var logger = new CaptureLogger<object>();
        var context = new DefaultHttpContext { TraceIdentifier = "correlation-123" };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        context.Request.Path = "/staffops/account/login";
        context.Request.QueryString = new QueryString("?password=do-not-log");
        SecurityEvents.Write(logger, context, "LoginFailed", "42");
        Assert.Equal("42", logger.Fields["MemberId"]);
        Assert.Equal("203.0.113.9", logger.Fields["SourceIp"]);
        Assert.Equal("correlation-123", logger.Fields["CorrelationId"]);
        Assert.Equal("/staffops/account/login", logger.Fields["Path"]);
        Assert.DoesNotContain("do-not-log", string.Join(" ", logger.Fields.Values));
    }

    [Theory]
    [InlineData(403, null, "AuthorizationDenied")]
    [InlineData(302, "https://localhost/Account/AccessDenied?returnUrl=x", "AuthorizationDenied")]
    [InlineData(429, null, "RateLimitRejected")]
    public async Task Authorization_and_rate_limit_failures_are_recorded(int status, string? location, string expected)
    {
        var logger = new CaptureLogger<SecurityEventMiddleware>();
        var middleware = new SecurityEventMiddleware(context =>
        {
            context.Response.StatusCode = status;
            if (location is not null) context.Response.Headers.Location = location;
            return Task.CompletedTask;
        }, logger);
        await middleware.InvokeAsync(new DefaultHttpContext());
        Assert.Equal(expected, logger.Fields["SecurityEvent"]);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public Dictionary<string, object?> Fields { get; private set; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(p => p.Key, p => p.Value);
    }
}
