using System.Net;

namespace ProgrammePulse.Services.Integrations.Resilience;

public enum HttpFailureKind
{
    /// <summary>2xx/3xx — not a failure.</summary>
    None,

    /// <summary>Worth retrying with backoff: the upstream is throttling or momentarily unavailable.</summary>
    Transient,

    /// <summary>Retrying can't help: bad credentials, missing resource, malformed request.</summary>
    Permanent
}

/// <summary>
/// One place that decides whether an upstream (ClickUp, Hub Planner) failure
/// is worth retrying. Used by TransientHttpRetryHandler; kept separate so
/// the classification is trivially unit-testable and so a sync service can
/// use the same vocabulary when it audits a failure.
/// </summary>
public static class HttpFailureClassifier
{
    public static HttpFailureKind Classify(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code < 400)
        {
            return HttpFailureKind.None;
        }

        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => HttpFailureKind.Transient,
            HttpStatusCode.TooManyRequests => HttpFailureKind.Transient,
            HttpStatusCode.InternalServerError => HttpFailureKind.Transient,
            HttpStatusCode.BadGateway => HttpFailureKind.Transient,
            HttpStatusCode.ServiceUnavailable => HttpFailureKind.Transient,
            HttpStatusCode.GatewayTimeout => HttpFailureKind.Transient,
            _ => HttpFailureKind.Permanent
        };
    }

    public static bool IsTransient(HttpStatusCode statusCode) => Classify(statusCode) == HttpFailureKind.Transient;
}
