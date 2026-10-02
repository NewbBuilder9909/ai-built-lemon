using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using ProgrammePulse.Middleware;

namespace ProgrammePulse.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public void Blank_inbound_id_is_replaced_with_a_generated_one()
    {
        var id = CorrelationIdMiddleware.ResolveCorrelationId(null);

        Assert.Equal(32, id.Length);
        Assert.True(Guid.TryParseExact(id, "N", out _));
    }

    [Fact]
    public void Well_formed_inbound_id_is_kept()
    {
        Assert.Equal("edge-7f3a:req_12.b", CorrelationIdMiddleware.ResolveCorrelationId(" edge-7f3a:req_12.b "));
    }

    [Theory]
    [InlineData("has spaces inside")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("line\nbreak")]
    public void Unsafe_inbound_id_is_discarded(string inbound)
    {
        var id = CorrelationIdMiddleware.ResolveCorrelationId(inbound);

        Assert.NotEqual(inbound, id);
        Assert.True(Guid.TryParseExact(id, "N", out _));
    }

    [Fact]
    public void Over_long_inbound_id_is_discarded()
    {
        var id = CorrelationIdMiddleware.ResolveCorrelationId(new string('a', 65));

        Assert.True(Guid.TryParseExact(id, "N", out _));
    }

    /// <summary>
    /// DefaultHttpContext's response feature discards OnStarting callbacks
    /// (nothing ever "starts" the response in a unit test), so this stand-in
    /// captures them and lets the test fire them the way Kestrel would just
    /// before the first byte is written.
    /// </summary>
    private sealed class CapturingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task FireOnStartingAsync()
        {
            foreach (var (callback, state) in _onStarting)
            {
                await callback(state);
            }
        }
    }

    [Fact]
    public async Task Sets_trace_identifier_and_echoes_header_on_the_response()
    {
        var context = new DefaultHttpContext();
        var responseFeature = new CapturingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        var nextCalled = false;

        var sut = new CorrelationIdMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context);
        await responseFeature.FireOnStartingAsync();

        Assert.True(nextCalled);
        Assert.Equal("abc-123", context.TraceIdentifier);
        Assert.Equal("abc-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }
}
