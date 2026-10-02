using System.Net;
using System.Text;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Integrations.GitHub;
using static ProgrammePulse.Tests.SkillsEvidence.FakeGitHubEvidenceClient;

namespace ProgrammePulse.Tests.SkillsEvidence;

/// <summary>
/// Anyone can write a commit that names a colleague as its author or
/// co-author. A signature GitHub has verified proves who made the commit (its
/// committer), so a commit row shows verified authorship only for that signer.
/// Every other commit row is kept and labelled "authorship not verified"
/// (Aikido: insufficient verification of data authenticity). Pull requests and
/// reviews are made from the person's own account and are not labelled.
/// </summary>
public class CommitAuthorshipTests
{
    private static readonly GitHubActor Alex = Human("1", "alex", "alex@acme.test");
    private static readonly GitHubActor Sam = Human("2", "sam", "sam@acme.test");

    private static GitHubCommit Signed(GitHubCommit commit, bool verified) =>
        commit with { SignatureVerified = verified, SignatureReason = verified ? "valid" : "unsigned" };

    private static async Task<IReadOnlyList<EngineeringEvidence>> IngestAsync(params GitHubCommit[] commits)
    {
        var ctx = new EvidenceTestContext();
        ctx.Client.WithCommits(EvidenceTestContext.RepoA, new GitHubPage<GitHubCommit>(commits, null, true));
        ctx.Client.WithPullRequests(EvidenceTestContext.RepoA, new GitHubPage<GitHubPullRequest>([PullRequest("pr1", 1, Alex)], null, true));
        await ctx.RunAsync(ctx.ConnectionA);
        return ctx.Repository.Evidence;
    }

    [Fact]
    public async Task A_verified_commit_signed_by_its_author_shows_verified_authorship()
    {
        var rows = await IngestAsync(Signed(Commit("sha1", Alex, Alex), verified: true));

        var author = Assert.Single(rows, r => r.Role == EvidenceRole.CommitAuthor);
        Assert.True(author.AuthorshipVerified);
        Assert.False(author.AuthorshipUnverified);
        Assert.Equal(EngineeringEvidenceSchema.CurrentVersion, author.SchemaVersion);
    }

    [Fact]
    public async Task An_unsigned_commit_is_kept_and_labelled_not_verified()
    {
        var rows = await IngestAsync(Signed(Commit("sha1", Alex, Alex), verified: false));

        var author = Assert.Single(rows, r => r.Role == EvidenceRole.CommitAuthor);
        Assert.False(author.AuthorshipVerified);
        Assert.True(author.AuthorshipUnverified);
    }

    [Fact]
    public async Task A_signer_naming_someone_else_as_author_verifies_only_themselves()
    {
        // Sam signs a commit that names Alex as author: the signature proves Sam made it.
        var rows = await IngestAsync(Signed(Commit("sha1", Alex, Sam), verified: true));

        Assert.True(Assert.Single(rows, r => r.Role == EvidenceRole.CommitAuthor).AuthorshipUnverified);
        Assert.True(Assert.Single(rows, r => r.Role == EvidenceRole.CommitCommitter).AuthorshipVerified);
    }

    [Fact]
    public async Task A_co_author_trailer_is_never_verified_even_on_a_signed_commit()
    {
        var rows = await IngestAsync(Signed(
            Commit("sha1", Alex, Alex, "Pairing\n\nCo-authored-by: Sam <sam@acme.test>"), verified: true));

        Assert.True(Assert.Single(rows, r => r.Role == EvidenceRole.CoAuthor).AuthorshipUnverified);
    }

    [Fact]
    public async Task A_commit_with_no_verification_recorded_reads_as_not_verified()
    {
        var rows = await IngestAsync(Commit("sha1", Alex, Alex));

        var author = Assert.Single(rows, r => r.Role == EvidenceRole.CommitAuthor);
        Assert.Null(author.AuthorshipVerified);
        Assert.True(author.AuthorshipUnverified);
    }

    [Fact]
    public async Task Pull_requests_are_authenticated_actions_and_are_not_labelled()
    {
        var rows = await IngestAsync();

        var pullRequest = Assert.Single(rows, r => r.Role == EvidenceRole.PullRequestAuthor);
        Assert.Null(pullRequest.AuthorshipVerified);
        Assert.False(pullRequest.AuthorshipUnverified);
    }

    [Fact]
    public async Task The_client_reads_githubs_verification_flag_and_reason()
    {
        // Shape from GitHub's REST "list commits" reference, not a live call.
        const string body = """
            [
              {"sha":"a1","html_url":"https://github.com/acme-ltd/web/commit/a1",
               "commit":{"message":"Signed","author":{"name":"Alex","email":"alex@acme.test","date":"2026-09-01T09:00:00Z"},
                         "committer":{"name":"Alex","email":"alex@acme.test","date":"2026-09-01T09:00:00Z"},
                         "verification":{"verified":true,"reason":"valid"}},
               "author":{"id":1,"login":"alex","type":"User"},"committer":{"id":1,"login":"alex","type":"User"}},
              {"sha":"b2","commit":{"message":"Unsigned","author":{"name":"Alex","email":"alex@acme.test","date":"2026-09-01T10:00:00Z"},
                         "verification":{"verified":false,"reason":"unsigned"}}},
              {"sha":"c3","commit":{"message":"Older shape","author":{"name":"Alex","email":"alex@acme.test","date":"2026-09-01T11:00:00Z"}}}
            ]
            """;
        var client = new GitHubEvidenceClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })));

        var page = await client.GetDefaultBranchCommitsAsync(EvidenceHostPolicy.GitHubDotComApi, "token", "acme-ltd/web", null, default);

        Assert.Equal([true, false, null], page.Items.Select(c => c.SignatureVerified));
        Assert.Equal(["valid", "unsigned", null], page.Items.Select(c => c.SignatureReason));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
