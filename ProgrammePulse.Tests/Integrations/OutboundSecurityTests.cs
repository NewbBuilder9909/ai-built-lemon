using System.Collections.Concurrent;
using System.Net;
using ProgrammePulse.Services.Integrations;
using ProgrammePulse.Services.Integrations.ClickUp;
using ProgrammePulse.Services.Integrations.HubPlanner;

namespace ProgrammePulse.Tests.Integrations;

public class OutboundSecurityTests
{
    [Theory]
    [InlineData("https://api.clickup.com/api/v2", "ClickUp", true)]
    [InlineData("https://api.hubplanner.com/v1/", "HubPlanner", true)]
    [InlineData("http://api.clickup.com/api/v2", "ClickUp", false)]
    [InlineData("https://127.0.0.1/api/v2", "ClickUp", false)]
    [InlineData("https://169.254.169.254/api/v2", "ClickUp", false)]
    [InlineData("https://api.clickup.com.evil.test/api/v2", "ClickUp", false)]
    [InlineData("https://api.clickup.com:8443/api/v2", "ClickUp", false)]
    [InlineData("https://secret@api.clickup.com/api/v2", "ClickUp", false)]
    [InlineData("https://api.clickup.com/api/v2?token=secret", "ClickUp", false)]
    [InlineData("https://api.clickup.com/api/v2#fragment", "ClickUp", false)]
    [InlineData("https://api.clickup.com/api/v20", "ClickUp", false)]
    [InlineData("https://api.clickup.com/api/v2/../admin", "ClickUp", false)]
    public void Base_urls_require_exact_provider_origin_and_path(string url, string provider, bool allowed) =>
        Assert.Equal(allowed, OutboundEndpointPolicy.IsAllowedBaseUrl(url, provider));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_tenants_share_transport_without_sharing_credentials(bool clickUp)
    {
        var observed = new ConcurrentBag<string>();
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            observed.Add(Assert.Single(request.Headers.GetValues("Authorization")));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(clickUp ? "{\"spaces\":[]}" : "[]") };
        })) { BaseAddress = new Uri(clickUp ? "https://api.clickup.com/api/v2/" : "https://api.hubplanner.com/v1/") };

        if (clickUp)
        {
            var client = new ClickUpApiClient(http);
            var a = client.WithCredential("tenant-a");
            var b = client.WithCredential("tenant-b");
            await Task.WhenAll(a.GetSpacesAsync("a"), b.GetSpacesAsync("b"));
        }
        else
        {
            var client = new HubPlannerApiClient(http);
            var a = client.WithCredential("tenant-a");
            var b = client.WithCredential("tenant-b");
            await Task.WhenAll(a.GetProjectsAsync(), b.GetProjectsAsync());
        }
        Assert.Equal(new[] { "tenant-a", "tenant-b" }, observed.Order().ToArray());
        Assert.False(http.DefaultRequestHeaders.Contains("Authorization"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unsafe_destinations_are_rejected_before_transport(bool clickUp)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })) { BaseAddress = new Uri("https://attacker.test/") };
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (clickUp) await new ClickUpApiClient(http).WithCredential("secret").GetSpacesAsync("1");
            else await new HubPlannerApiClient(http).WithCredential("secret").GetProjectsAsync();
        });
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Transport_does_not_follow_redirects_or_pool_cookies()
    {
        using var handler = OutboundEndpointPolicy.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
