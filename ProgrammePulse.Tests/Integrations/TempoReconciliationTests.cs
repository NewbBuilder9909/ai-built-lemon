using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ProgrammePulse.Services.Integrations.Tempo;

namespace ProgrammePulse.Tests.Integrations;

public sealed class TempoReconciliationTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Duplicate_worklogs_are_counted_once_and_explicit_deletions_win()
    {
        var one = Worklog(1, 7200);
        var result = TempoReconciliationBatch.Build([one, one, Worklog(2, 3600)], [Deletion(2), Deletion(2)], From, Now);
        Assert.Equal(1, Assert.Single(result.Worklogs).Fact.Id);
        Assert.Equal(7200, result.Worklogs[0].Fact.Seconds);
        Assert.Equal(1, result.DuplicateWorklogs);
        Assert.Equal(1, result.DuplicateDeletions);
        Assert.Equal(1, result.SuppressedWorklogs);
        Assert.Equal(2, Assert.Single(result.Deletions).Id);
    }

    [Fact]
    public void Conflicting_duplicates_fail_instead_of_choosing_a_pagination_order() =>
        Assert.Throws<JsonException>(() => TempoReconciliationBatch.Build([Worklog(1, 3600), Worklog(1, 7200)], [], null, Now));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"missing\"")]
    [InlineData("null")]
    public void Invalid_worklog_IDs_cannot_reach_persistence(string id) =>
        Assert.Throws<JsonException>(() => TempoReconciliationBatch.Build([Json($$"""{"tempoWorklogId":{{id}},"timeSpentSeconds":3600,"startDate":"2026-09-23"}""")], [], null, Now));

    [Theory]
    [InlineData("-1", "2026-09-23")]
    [InlineData("1.5", "2026-09-23")]
    [InlineData("2147483648", "2026-09-23")]
    [InlineData("null", "2026-09-23")]
    [InlineData("3600", "2026-02-30")]
    public void Invalid_durations_and_business_dates_fail_the_batch(string seconds, string date) =>
        Assert.Throws<JsonException>(() => TempoReconciliationBatch.Build([Json($$"""{"tempoWorklogId":1,"timeSpentSeconds":{{seconds}},"startDate":"{{date}}"}""")], [], null, Now));

    [Theory]
    [InlineData("2026-08-31T23:59:59Z")]
    [InlineData("2026-09-24T12:00:01Z")]
    [InlineData("bad-date")]
    public void Deletions_require_a_valid_timestamp_inside_the_audited_window(string at) =>
        Assert.Throws<JsonException>(() => TempoReconciliationBatch.Build([], [Deletion(1, at)], From, Now));

    [Fact]
    public void Deletion_cannot_be_applied_when_the_audit_is_disabled() =>
        Assert.Throws<JsonException>(() => TempoReconciliationBatch.Build([], [Deletion(1)], null, Now));

    [Fact]
    public void Work_dates_and_IDs_do_not_depend_on_the_process_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cy-GB");
            var batch = TempoReconciliationBatch.Build([Worklog(9, 0)], [Deletion(1, "2026-09-01T00:00:00")], From, Now);
            Assert.Equal(new DateOnly(2026, 9, 23), batch.Worklogs[0].Fact.WorkDate);
            Assert.Equal(TimeSpan.Zero, batch.Deletions[0].DeletedAtUtc.Offset);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(90, true)]
    [InlineData(180, true)]
    [InlineData(181, false)]
    public void Configuration_requires_explicit_tenant_opt_in_and_a_bounded_window(int days, bool valid)
    {
        var options = new TempoReconciliationOptions { DeletionLookbackDays = days };
        Assert.False(options.IsEnabled(Guid.NewGuid()));
        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], true));
        var tenant = Guid.NewGuid();
        options.EnabledTenantIds = [tenant];
        Assert.True(options.IsEnabled(tenant));
        Assert.False(options.IsEnabled(Guid.NewGuid()));
    }

    [Fact]
    public async Task Audit_pages_retain_the_window_and_token_and_accept_an_opaque_continuation_key()
    {
        var requests = new List<Uri>();
        using var http = Client(request =>
        {
            Assert.Equal("tenant-token", request.Headers.Authorization?.Parameter);
            requests.Add(request.RequestUri!);
            return Response(requests.Count == 1
                ? """{"results":[{"tempoWorklogId":1}],"metadata":{"lastEvaluatedKey":"opaque/a+b="}}"""
                : """{"results":[{"tempoWorklogId":2}],"metadata":{}}""");
        });
        var rows = await new TempoApiClient(http).GetDeletedWorklogsAsync("tenant-token", From);
        Assert.Equal(2, rows.Count);
        Assert.All(requests, uri => Assert.Equal("/audit/1/events/deleted/types/worklog", uri.AbsolutePath));
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(requests[1].Query);
        Assert.Equal("opaque/a+b=", query["lastEvaluatedKey"]);
        Assert.Equal("2026-09-01T00:00:00", query["updatedFrom"]);
    }

    [Theory]
    [InlineData("https://attacker.example/audit/1/events/deleted/types/worklog?updatedFrom=2026-09-01T00:00:00")]
    [InlineData("https://api.tempo.io/4/worklogs?updatedFrom=2026-09-01T00:00:00")]
    [InlineData("http://api.tempo.io/audit/1/events/deleted/types/worklog?updatedFrom=2026-09-01T00:00:00")]
    [InlineData("https://api.tempo.io/audit/1/events/deleted/types/worklog?updatedFrom=2026-09-02T00:00:00")]
    [InlineData("https://api.tempo.io/audit/1/events/deleted/types/worklog?offset=1000")]
    [InlineData("https://api.tempo.io/audit/1/events/deleted/types/worklog?updatedFrom=2026-09-01T00:00:00&project=other")]
    public async Task Unsafe_or_scope_changing_audit_pages_fail_before_sending_another_request(string next)
    {
        var sent = 0;
        using var http = Client(_ => { sent++; return Response(JsonSerializer.Serialize(new { results = Array.Empty<object>(), metadata = new { next } })); });
        await Assert.ThrowsAsync<JsonException>(() => new TempoApiClient(http).GetDeletedWorklogsAsync("tenant-token", From));
        Assert.Equal(1, sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"results\":[],\"metadata\":null}")]
    [InlineData("{\"results\":[],\"metadata\":{\"next\":42}}")]
    public async Task Malformed_pages_are_not_interpreted_as_a_complete_empty_audit(string payload)
    {
        using var http = Client(_ => Response(payload));
        await Assert.ThrowsAsync<JsonException>(() => new TempoApiClient(http).GetDeletedWorklogsAsync("tenant-token", From));
    }

    [Fact]
    public async Task Repeated_audit_cursor_fails_instead_of_looping()
    {
        var sent = 0;
        using var http = Client(_ => { sent++; return Response("""{"results":[],"metadata":{"lastEvaluatedKey":"repeat"}}"""); });
        await Assert.ThrowsAsync<JsonException>(() => new TempoApiClient(http).GetDeletedWorklogsAsync("tenant-token", From));
        Assert.Equal(2, sent);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Upstream_failures_do_not_become_empty_deletion_lists(int status)
    {
        using var http = Client(_ => new HttpResponseMessage((HttpStatusCode)status));
        await Assert.ThrowsAsync<HttpRequestException>(() => new TempoApiClient(http).GetDeletedWorklogsAsync("tenant-token", From));
    }

    private static JsonElement Worklog(long id, long seconds) => JsonSerializer.SerializeToElement(new
    {
        tempoWorklogId = id, timeSpentSeconds = seconds, startDate = "2026-09-23",
        issue = new { id = "100" }, author = new { accountId = "account" }
    });
    private static JsonElement Deletion(long id, string at = "2026-09-23T10:00:00Z") =>
        JsonSerializer.SerializeToElement(new { tempoWorklogId = id.ToString(CultureInfo.InvariantCulture), deletedAt = at });
    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response)) { BaseAddress = new("https://api.tempo.io/") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
