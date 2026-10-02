using System.Net;
using System.Text;
using ProgrammePulse.Services.Integrations.Jira;
using ProgrammePulse.Services.Integrations.Tempo;

namespace ProgrammePulse.Tests.Integrations;

public sealed class JiraTempoApiClientTests
{
    [Fact]
    public async Task JiraSearchPagesStayOnSelectedProjectsAndUseBearerPerRequest()
    {
        var requests = new List<(Uri Uri, string? Token, string Body)>();
        using var http = new HttpClient(new StubHandler(async request =>
        {
            requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter,
                await request.Content!.ReadAsStringAsync()));
            return Json(requests.Count == 1
                ? """{"issues":[{"id":"1"}],"nextPageToken":"next"}"""
                : """{"issues":[{"id":"2"}]}""");
        })) { BaseAddress = new Uri("https://api.atlassian.com/") };
        var result = await new JiraApiClient(http).GetIssuesAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            [10001], "tenant-token");
        Assert.Equal(2, result.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("api.atlassian.com", request.Uri.Host);
            Assert.Equal("tenant-token", request.Token);
            Assert.Contains("project in (10001)", request.Body);
        });
        Assert.Contains("next", requests[1].Body);
    }

    [Fact]
    public async Task TempoRejectsCrossHostPaginationBeforeSendingToken()
    {
        var sent = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            sent++;
            return Task.FromResult(Json("""{"results":[],"metadata":{"next":"https://attacker.example/4/worklogs"}}"""));
        })) { BaseAddress = new Uri("https://api.tempo.io/") };
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new TempoApiClient(http).GetWorklogsAsync("tenant-token"));
        Assert.Equal(1, sent);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
