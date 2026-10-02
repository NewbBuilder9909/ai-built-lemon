using System.Net.Http.Headers;

namespace ProgrammePulse.Services.Integrations.Resilience;

/// <summary>
/// DelegatingHandler shared by every Bronze typed HttpClient (ClickUp, Hub
/// Planner — see ProgrammeOperationsComposer). Retries a request up to
/// <see cref="DefaultMaxRetries"/> times when the failure is transient per
/// HttpFailureClassifier (429/5xx/408), when the connection failed
/// (HttpRequestException), or when HttpClient's own timeout fired; never on a
/// permanent status (401/403/404/400…) and never when the *caller* cancelled.
///
/// Backoff is exponential from <see cref="DefaultBaseDelay"/> (1s, 2s, 4s),
/// except that an upstream Retry-After header (delta or date) is honoured
/// instead — that's how both ClickUp and Hub Planner communicate their burst
/// limits. Every wait is capped at <see cref="MaxDelay"/> so a hostile or
/// broken Retry-After can't park a sync for an hour.
///
/// Hand-rolled rather than Polly: the two integrations only issue GETs (safe
/// to replay) and this codebase avoids new NuGet dependencies for things a
/// page of code covers. The delay function is injectable so the tests run
/// without sleeping.
/// </summary>
public sealed class TransientHttpRetryHandler : DelegatingHandler
{
    public const int DefaultMaxRetries = 3;
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public TransientHttpRetryHandler() : this(DefaultMaxRetries, DefaultBaseDelay, Task.Delay)
    {
    }

    public TransientHttpRetryHandler(int maxRetries, TimeSpan baseDelay, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay;
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transientException = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                transientException = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout surfaces as TaskCanceledException without
                // the caller's token being cancelled — that's a slow upstream,
                // not a caller abort, so it's retried like any other transient.
                transientException = ex;
            }

            if (response is not null && !HttpFailureClassifier.IsTransient(response.StatusCode))
            {
                return response;
            }

            if (attempt >= _maxRetries)
            {
                if (response is not null)
                {
                    return response;
                }

                throw transientException!;
            }

            var wait = ComputeDelay(attempt, response?.Headers.RetryAfter, _baseDelay, DateTimeOffset.UtcNow);
            response?.Dispose();
            await _delay(wait, cancellationToken);
        }
    }

    /// <summary>Exposed for tests: Retry-After wins when present, otherwise baseDelay × 2^attempt; always capped at <see cref="MaxDelay"/>.</summary>
    public static TimeSpan ComputeDelay(int attempt, RetryConditionHeaderValue? retryAfter, TimeSpan baseDelay, DateTimeOffset nowUtc)
    {
        TimeSpan? fromHeader = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - nowUtc,
            _ => null
        };

        var wait = fromHeader is { } header && header > TimeSpan.Zero
            ? header
            : TimeSpan.FromTicks(baseDelay.Ticks * (1L << Math.Min(attempt, 10)));

        return wait > MaxDelay ? MaxDelay : wait;
    }
}
