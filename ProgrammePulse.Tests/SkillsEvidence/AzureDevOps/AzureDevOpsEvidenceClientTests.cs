using System.Net;
using System.Text;
using ProgrammePulse.Services.Integrations.AzureDevOps;

namespace ProgrammePulse.Tests.SkillsEvidence.AzureDevOps;

/// <summary>
/// The HTTP client against canned Azure DevOps responses. The shapes are
/// from the REST 7.1 reference, not from a live organisation — so these
/// prove the client's own rules (where the token goes, how a refusal is
/// recognised, how paging and cursors behave), not that the vendor
/// behaves as documented. That needs a sandbox run.
/// </summary>
public sealed class AzureDevOpsEvidenceClientTests
{
    private const string Base = "https://dev.azure.com/acme";

    private static readonly string RepositoryJson =
        """{"id":"r1","name":"portal","defaultBranch":"refs/heads/main","isDisabled":false,"project":{"name":"Web"}}""";

    // ---- token refusal ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(HttpStatusCode.Found)]
    public async Task Every_way_azure_devops_refuses_a_token_reads_as_a_refusal(HttpStatusCode status)
    {
        var client = Client(_ => new HttpResponseMessage(status) { Content = Html() });

        Assert.Equal(AzureDevOpsVerification.TokenRejected, await client.VerifyOrganisationAsync(Base, "pat", default));
    }

    [Fact]
    public async Task A_sign_in_page_served_with_200_is_a_refusal_not_an_empty_result()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = Html() });

        Assert.Equal(AzureDevOpsVerification.TokenRejected, await client.VerifyOrganisationAsync(Base, "pat", default));
    }

    [Fact]
    public async Task An_unknown_organisation_is_distinguished_from_a_refused_token()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(AzureDevOpsVerification.OrganisationNotFound, await client.VerifyOrganisationAsync(Base, "pat", default));
    }

    [Fact]
    public async Task The_token_is_sent_per_request_as_basic_auth_to_dev_azure_com_only()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request => { seen = request; return Json("""{"count":1,"value":[{"name":"Web"}]}"""); });

        Assert.Equal(AzureDevOpsVerification.Valid, await client.VerifyOrganisationAsync(Base, "secret-pat", default));

        Assert.Equal("dev.azure.com", seen!.RequestUri!.Host);
        Assert.StartsWith("/acme/_apis/projects", seen.RequestUri.AbsolutePath);
        Assert.Equal("Basic", seen.Headers.Authorization!.Scheme);
        Assert.Equal(":secret-pat", Encoding.UTF8.GetString(Convert.FromBase64String(seen.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task A_tampered_base_url_is_refused_before_anything_is_sent()
    {
        var sent = 0;
        var client = Client(_ => { sent++; return Json("{}"); });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.VerifyOrganisationAsync("https://attacker.example/acme", "pat", default));
        Assert.Equal(0, sent);
    }

    // ---- repositories ----

    [Fact]
    public async Task Disabled_and_empty_repositories_are_not_offered()
    {
        var client = Client(_ => Json("""
            {"value":[
              {"id":"1","name":"portal","defaultBranch":"refs/heads/main","project":{"name":"Web"}},
              {"id":"2","name":"old","defaultBranch":"refs/heads/main","isDisabled":true,"project":{"name":"Web"}},
              {"id":"3","name":"empty","project":{"name":"Web"}}
            ]}
            """));

        var repositories = await client.GetAccessibleRepositoriesAsync(Base, "pat", default);

        Assert.Equal(["Web/portal"], repositories.Select(r => r.Key));
    }

    // ---- commits ----

    [Fact]
    public async Task Commits_page_until_a_short_page_and_resume_from_the_newest_committer_date()
    {
        var requests = new List<Uri>();
        var client = Client(request =>
        {
            requests.Add(request.RequestUri!);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal))
            {
                return Json(RepositoryJson);
            }

            var skip = request.RequestUri.Query.Contains("%24skip=100") || request.RequestUri.Query.Contains("$skip=100");
            return Json(skip ? Commits(1, day: 5) : Commits(100, day: 3));
        });

        var page = await client.GetDefaultBranchCommitsAsync(Base, "pat", "Web/portal", cursor: null, default);

        Assert.True(page.IsComplete);
        Assert.Equal(101, page.Items.Count);
        Assert.Equal("2026-09-05T10:00:00.0000000Z", page.NextCursor);
        Assert.All(requests, uri => Assert.Equal("dev.azure.com", uri.Host));
        Assert.Contains("searchCriteria.itemVersion.version=main", requests[1].Query);
        Assert.DoesNotContain("fromDate", requests[1].Query);
    }

    [Fact]
    public async Task A_resumed_commit_read_overlaps_the_cursor_by_a_minute()
    {
        Uri? commitsRequest = null;
        var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal))
            {
                commitsRequest = request.RequestUri;
                return Json("""{"value":[]}""");
            }

            return Json(RepositoryJson);
        });

        await client.GetDefaultBranchCommitsAsync(Base, "pat", "Web/portal", "2026-09-05T10:00:00.0000000Z", default);

        Assert.Contains("fromDate=2026-09-05T09%3A59%3A00", commitsRequest!.Query);
    }

    [Fact]
    public async Task A_failed_page_keeps_what_was_read_and_does_not_advance_the_cursor()
    {
        var client = Client(request =>
            !request.RequestUri!.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal) ? Json(RepositoryJson)
            : request.RequestUri.Query.Contains("skip=100") ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(Commits(100, day: 3)));

        var page = await client.GetDefaultBranchCommitsAsync(Base, "pat", "Web/portal", cursor: null, default);

        Assert.False(page.IsComplete);
        Assert.Equal(100, page.Items.Count);
        Assert.Null(page.NextCursor);
        Assert.NotNull(page.IncompleteReason);
    }

    [Fact]
    public async Task A_repository_that_has_gone_is_lost_access_not_an_empty_history()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<AzureDevOpsAccessLostException>(() =>
            client.GetDefaultBranchCommitsAsync(Base, "pat", "Web/portal", null, default));
    }

    [Fact]
    public async Task A_token_refused_mid_run_is_lost_access_too()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation) { Content = Html() });

        await Assert.ThrowsAsync<AzureDevOpsAccessLostException>(() =>
            client.GetCompletedPullRequestsAsync(Base, "pat", "Web/portal", null, default));
    }

    [Fact]
    public async Task Project_names_with_spaces_are_escaped_into_the_path()
    {
        Uri? seen = null;
        var client = Client(request => { seen ??= request.RequestUri; return Json("""{"value":[]}"""); });

        await client.GetCompletedPullRequestsAsync(Base, "pat", "Web Platform/api", null, default);

        Assert.StartsWith("/acme/Web%20Platform/_apis/git/repositories/api/pullrequests", seen!.AbsolutePath);
    }

    // ---- pull requests ----

    [Fact]
    public async Task Pull_requests_carry_reviewer_votes_and_links_built_from_the_validated_base()
    {
        var client = Client(_ => Json("""
            {"value":[{
              "pullRequestId":41,"title":"Add export","closedDate":"2026-09-02T09:00:00Z",
              "url":"https://attacker.example/pr/41",
              "createdBy":{"id":"u-alex","displayName":"Alex","uniqueName":"alex@acme.test"},
              "reviewers":[
                {"id":"u-sarah","displayName":"Sarah","uniqueName":"sarah@acme.test","vote":10},
                {"id":"g-web","displayName":"Web Team","uniqueName":"[Web]\\Web Team","vote":10,"isContainer":true}
              ]}]}
            """));

        var page = await client.GetCompletedPullRequestsAsync(Base, "pat", "Web/portal", null, default);

        var pr = Assert.Single(page.Items);
        Assert.Equal("https://dev.azure.com/acme/Web/_git/portal/pullrequest/41", pr.WebUrl);
        Assert.Equal("alex@acme.test", pr.CreatedBy!.Email);
        Assert.Equal(2, pr.Reviewers.Count);
        Assert.Contains(pr.Reviewers, r => r.Identity.Id == "u-sarah" && r.Vote == 10 && !r.Identity.IsContainer);
        Assert.Contains(pr.Reviewers, r => r.Identity.Id == "g-web" && r.Identity.IsContainer && r.Identity.Email is null);
        Assert.Equal("2026-09-02T09:00:00.0000000Z", page.NextCursor);
    }

    [Fact]
    public async Task A_resumed_pull_request_read_filters_by_close_time_on_both_sides()
    {
        Uri? seen = null;
        var client = Client(request =>
        {
            seen = request.RequestUri;
            return Json("""
                {"value":[
                  {"pullRequestId":1,"closedDate":"2026-08-01T09:00:00Z","createdBy":{"id":"u1","uniqueName":"a@acme.test"}},
                  {"pullRequestId":2,"closedDate":"2026-09-10T09:00:00Z","createdBy":{"id":"u1","uniqueName":"a@acme.test"}}
                ]}
                """);
        });

        var page = await client.GetCompletedPullRequestsAsync(Base, "pat", "Web/portal", "2026-09-05T00:00:00.0000000Z", default);

        Assert.Contains("queryTimeRangeType=closed", seen!.Query);
        Assert.Contains("minTime=", seen.Query);
        Assert.Equal([2], page.Items.Select(p => p.PullRequestId));
    }

    // ---- helpers ----

    private static AzureDevOpsEvidenceClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static StringContent Html() => new("<html>Sign in</html>", Encoding.UTF8, "text/html");

    private static string Commits(int count, int day)
    {
        var items = Enumerable.Range(0, count).Select(i =>
            $$$"""{"commitId":"c{{{day}}}-{{{i}}}","comment":"Change {{{i}}}","author":{"name":"Alex","email":"alex@acme.test","date":"2026-09-0{{{day}}}T09:00:00Z"},"committer":{"name":"Alex","email":"alex@acme.test","date":"2026-09-0{{{day}}}T10:00:00Z"}}""");
        return $$"""{"count":{{count}},"value":[{{string.Join(",", items)}}]}""";
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
