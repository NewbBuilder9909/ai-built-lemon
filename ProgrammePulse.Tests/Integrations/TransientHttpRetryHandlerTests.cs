using System.Net;
using System.Net.Http.Headers;
using ProgrammePulse.Services.Integrations.Resilience;

namespace ProgrammePulse.Tests.Integrations;

public class TransientHttpRetryHandlerTests
{
    /// <summary>Scripted inner handler: each call dequeues the next response or exception.</summary>
    private sealed class ScriptedHandler(params object[] script) : HttpMessageHandler
    {
        private readonly Queue<object> _script = new(script);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var next = _script.Dequeue();
            return next switch
            {
                HttpResponseMessage response => Task.FromResult(response),
                Exception exception => Task.FromException<HttpResponseMessage>(exception),
                _ => throw new InvalidOperationException("Script entries must be responses or exceptions.")
            };
        }
    }

    private static (HttpClient Client, ScriptedHandler Inner, List<TimeSpan> Delays) Build(params object[] script)
    {
        var inner = new ScriptedHandler(script);
        var delays = new List<TimeSpan>();
        var sut = new TransientHttpRetryHandler(3, TimeSpan.FromSeconds(1), (wait, _) =>
        {
            delays.Add(wait);
            return Task.CompletedTask;
        })
        {
            InnerHandler = inner
        };
        return (new HttpClient(sut) { BaseAddress = new Uri("https://example.test/") }, inner, delays);
    }

    private static HttpResponseMessage Status(HttpStatusCode code, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(code);
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, HttpFailureKind.None)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpFailureKind.Transient)]
    [InlineData(HttpStatusCode.GatewayTimeout, HttpFailureKind.Transient)]
    [InlineData(HttpStatusCode.RequestTimeout, HttpFailureKind.Transient)]
    [InlineData(HttpStatusCode.Unauthorized, HttpFailureKind.Permanent)]
    [InlineData(HttpStatusCode.Forbidden, HttpFailureKind.Permanent)]
    [InlineData(HttpStatusCode.NotFound, HttpFailureKind.Permanent)]
    [InlineData(HttpStatusCode.BadRequest, HttpFailureKind.Permanent)]
    public void Classifier_separates_transient_from_permanent(HttpStatusCode code, HttpFailureKind expected) =>
        Assert.Equal(expected, HttpFailureClassifier.Classify(code));

    [Fact]
    public async Task Retries_a_429_honouring_retry_after_then_returns_the_success()
    {
        var (client, inner, delays) = Build(Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(2)), Status(HttpStatusCode.OK));

        var response = await client.GetAsync("team");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_returns_the_last_transient_response()
    {
        var (client, inner, delays) = Build(
            Status(HttpStatusCode.InternalServerError),
            Status(HttpStatusCode.BadGateway),
            Status(HttpStatusCode.ServiceUnavailable),
            Status(HttpStatusCode.GatewayTimeout));

        var response = await client.GetAsync("team");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(4, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], delays);
    }

    [Fact]
    public async Task Does_not_retry_a_permanent_failure()
    {
        var (client, inner, delays) = Build(Status(HttpStatusCode.Unauthorized), Status(HttpStatusCode.OK));

        var response = await client.GetAsync("team");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Recovers_from_a_connection_failure()
    {
        var (client, inner, _) = Build(new HttpRequestException("connection reset"), Status(HttpStatusCode.OK));

        var response = await client.GetAsync("team");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Rethrows_the_connection_failure_once_retries_are_exhausted()
    {
        var (client, inner, _) = Build(
            new HttpRequestException("1"), new HttpRequestException("2"), new HttpRequestException("3"), new HttpRequestException("4"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("team"));
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task Does_not_retry_when_the_caller_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var inner = new ScriptedHandler(new TaskCanceledException("caller cancelled"));
        var delays = new List<TimeSpan>();
        var sut = new TransientHttpRetryHandler(3, TimeSpan.FromSeconds(1), (wait, _) => { delays.Add(wait); return Task.CompletedTask; }) { InnerHandler = inner };
        var client = new HttpClient(sut) { BaseAddress = new Uri("https://example.test/") };
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("team", cts.Token));

        Assert.Equal(1, inner.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public void Delay_is_capped_even_when_retry_after_is_absurd()
    {
        var wait = TransientHttpRetryHandler.ComputeDelay(0, new RetryConditionHeaderValue(TimeSpan.FromHours(1)), TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);

        Assert.Equal(TransientHttpRetryHandler.MaxDelay, wait);
    }

    [Fact]
    public void Delay_uses_a_retry_after_date_relative_to_now()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

        var wait = TransientHttpRetryHandler.ComputeDelay(0, new RetryConditionHeaderValue(now.AddSeconds(7)), TimeSpan.FromSeconds(1), now);

        Assert.Equal(TimeSpan.FromSeconds(7), wait);
    }
}
