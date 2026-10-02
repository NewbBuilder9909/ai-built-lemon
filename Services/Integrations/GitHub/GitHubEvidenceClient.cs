using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ProgrammePulse.Models.Integrations.GitHub.Raw;
using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.Integrations.GitHub;

/// <summary>
/// Read-only GitHub REST client. Authentication is supplied per request so
/// a tenant's token never enters pooled HttpClient defaults — the same
/// rule JiraApiClient follows, and it matters more here because several
/// tenants' installations share this client.
///
/// Three things this deliberately does not do: follow the API's own
/// pagination <c>Link</c> URLs blindly (they are re-derived against the
/// validated base URL instead, so a redirected or spoofed Link header
/// cannot move the request to another host), fetch patches or diffs, or
/// give up quietly. A page that fails returns
/// <see cref="GitHubPage{T}.IsComplete"/> false with a reason, and the
/// caller records partial coverage rather than treating the absence of
/// results as an absence of work.
/// </summary>
public sealed class GitHubEvidenceClient(HttpClient client) : IGitHubEvidenceClient
{
    private const int PageSize = 100;
    private const int MaxPagesPerStream = 50;
    private const string ApiVersion = "2022-11-28";

    /// <summary>Commits are windowed by "since"; PRs by updated-descending with an early stop.</summary>
    private const int MaxChangedPathsPerArtifact = 300;

    public async Task<string?> VerifyInstallationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(apiBaseUrl, "installation/repositories?per_page=1", accessToken, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (!root.TryGetProperty("repositories", out var repositories) || repositories.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var repository in repositories.EnumerateArray())
        {
            var owner = Text(Child(repository, "owner"), "login");
            if (!string.IsNullOrWhiteSpace(owner))
            {
                return owner;
            }
        }

        // A grant with no repositories is valid but useless; the caller
        // reports it rather than pretending the connection works.
        return null;
    }

    public async Task<IReadOnlyList<string>> GetAccessibleRepositoriesAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        var accessible = new List<string>();

        for (var page = 1; page <= MaxPagesPerStream; page++)
        {
            using var document = await GetAsync(
                apiBaseUrl, $"installation/repositories?per_page={PageSize}&page={page}", accessToken, cancellationToken);
            if (document is null)
            {
                break;
            }

            if (!document.RootElement.TryGetProperty("repositories", out var repositories)
                || repositories.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var before = accessible.Count;
            foreach (var repository in repositories.EnumerateArray())
            {
                var fullName = Text(repository, "full_name");
                if (EvidenceHostPolicy.IsValidRepositoryKey(fullName))
                {
                    accessible.Add(fullName!.ToLowerInvariant());
                }
            }

            if (accessible.Count - before < PageSize)
            {
                break;
            }
        }

        return accessible;
    }

    public async Task<GitHubPage<GitHubCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        // The cursor is an ISO timestamp. Overlapping by a minute is
        // deliberate: GitHub orders by commit date, which a rebase can move
        // backwards, so resuming exactly at the last timestamp can skip a
        // commit. The upsert is idempotent, so the overlap costs nothing.
        var since = ParseCursor(cursor)?.AddMinutes(-1);
        var sinceQuery = since is null ? string.Empty : $"&since={Iso(since.Value)}";

        var commits = new List<GitHubCommit>();
        DateTime? newest = since;

        for (var page = 1; page <= MaxPagesPerStream; page++)
        {
            JsonDocument? document;
            try
            {
                document = await GetAsync(
                    apiBaseUrl, $"repos/{repositoryKey}/commits?per_page={PageSize}&page={page}{sinceQuery}",
                    accessToken, cancellationToken, repositoryKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Everything fetched so far is kept; the cursor is not
                // advanced past it, so the next run re-reads from here.
                return new GitHubPage<GitHubCommit>(commits, null, false, Describe(ex));
            }

            if (document is null)
            {
                return new GitHubPage<GitHubCommit>(commits, null, false, "the repository could not be read");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new GitHubPage<GitHubCommit>(commits, null, false, "unexpected response shape");
                }

                var count = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    count++;
                    var commit = MapCommit(element);
                    if (commit is null)
                    {
                        continue;
                    }

                    commits.Add(commit);
                    if (commit.AuthoredAtUtc is { } authored && (newest is null || authored > newest))
                    {
                        newest = authored;
                    }
                }

                if (count < PageSize)
                {
                    return new GitHubPage<GitHubCommit>(commits, newest is null ? cursor : Iso(newest.Value), true);
                }
            }
        }

        // Hit the page ceiling: there is more, and saying so is the honest
        // answer. The cursor advances so the next run continues.
        return new GitHubPage<GitHubCommit>(
            commits, newest is null ? cursor : Iso(newest.Value), false, "more history remains beyond this run's page limit");
    }

    public async Task<GitHubPage<GitHubPullRequest>> GetMergedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        var since = ParseCursor(cursor);
        var pullRequests = new List<GitHubPullRequest>();
        DateTime? newest = since;

        for (var page = 1; page <= MaxPagesPerStream; page++)
        {
            JsonDocument? document;
            try
            {
                document = await GetAsync(
                    apiBaseUrl,
                    $"repos/{repositoryKey}/pulls?state=closed&sort=updated&direction=desc&per_page={PageSize}&page={page}",
                    accessToken, cancellationToken, repositoryKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                return new GitHubPage<GitHubPullRequest>(pullRequests, null, false, Describe(ex));
            }

            if (document is null)
            {
                return new GitHubPage<GitHubPullRequest>(pullRequests, null, false, "the repository could not be read");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new GitHubPage<GitHubPullRequest>(pullRequests, null, false, "unexpected response shape");
                }

                var count = 0;
                var reachedCursor = false;

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    count++;
                    var updated = Timestamp(element, "updated_at");

                    // Sorted by updated descending, so once we are behind
                    // the cursor everything after it is older too.
                    if (since is not null && updated is not null && updated <= since)
                    {
                        reachedCursor = true;
                        break;
                    }

                    // Closed-not-merged is not delivery evidence. A closed
                    // PR that was never merged says nothing about work that
                    // reached the default branch.
                    var mergedAt = Timestamp(element, "merged_at");
                    if (mergedAt is null)
                    {
                        continue;
                    }

                    var pullRequest = MapPullRequest(element, mergedAt.Value);
                    if (pullRequest is null)
                    {
                        continue;
                    }

                    pullRequests.Add(pullRequest);
                    if (updated is { } stamp && (newest is null || stamp > newest))
                    {
                        newest = stamp;
                    }
                }

                if (reachedCursor || count < PageSize)
                {
                    return new GitHubPage<GitHubPullRequest>(pullRequests, newest is null ? cursor : Iso(newest.Value), true);
                }
            }
        }

        return new GitHubPage<GitHubPullRequest>(
            pullRequests, newest is null ? cursor : Iso(newest.Value), false, "more pull requests remain beyond this run's page limit");
    }

    public async Task<GitHubPage<GitHubReview>> GetReviewsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, IReadOnlyList<int> pullRequestNumbers, CancellationToken cancellationToken)
    {
        var reviews = new List<GitHubReview>();

        foreach (var number in pullRequestNumbers.Distinct().Take(MaxPagesPerStream * 2))
        {
            JsonDocument? document;
            try
            {
                document = await GetAsync(
                    apiBaseUrl, $"repos/{repositoryKey}/pulls/{number}/reviews?per_page={PageSize}",
                    accessToken, cancellationToken, repositoryKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                return new GitHubPage<GitHubReview>(reviews, null, false, Describe(ex));
            }

            if (document is null)
            {
                return new GitHubPage<GitHubReview>(reviews, null, false, "the repository could not be read");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    var id = Text(element, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    reviews.Add(new GitHubReview(
                        id!,
                        number.ToString(CultureInfo.InvariantCulture),
                        number,
                        Text(element, "state"),
                        Timestamp(element, "submitted_at"),
                        MapActor(Child(element, "user")),
                        Text(element, "html_url")));
                }
            }
        }

        return new GitHubPage<GitHubReview>(reviews, null, true);
    }

    // ---- HTTP ----

    /// <summary>
    /// Null means "this repository or installation cannot be read right
    /// now, and it is not a transport error" — a 404 on a repository we
    /// were granted is how GitHub reports a revoked permission.
    /// </summary>
    private async Task<JsonDocument?> GetAsync(
        string apiBaseUrl, string relativePath, string accessToken, CancellationToken cancellationToken, string? repositoryKey = null)
    {
        // The base URL came from EvidenceHostPolicy. Re-validating here
        // means no caller can bypass the allow-list by constructing a
        // client call directly.
        var canonical = EvidenceHostPolicy.Canonicalize(apiBaseUrl, AllowedEnterpriseHosts)
            ?? throw new InvalidOperationException("The evidence connection's API host is not permitted.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{canonical}/{relativePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("ProgrammePulse-Evidence");

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && repositoryKey is not null)
        {
            // 403 here is usually a rate limit, which is transient, and
            // 404 is usually a lost permission, which is not. The
            // remaining-quota header is what separates them.
            if (response.StatusCode == HttpStatusCode.Forbidden && RemainingRateLimit(response) == 0)
            {
                throw new HttpRequestException("the GitHub rate limit was reached");
            }

            throw new GitHubAccessLostException(repositoryKey);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enterprise hosts come from deployment configuration only. Empty here
    /// and supplied by the ingestion service, which reads options — the
    /// client never invents a host.
    /// </summary>
    public IReadOnlyList<string> AllowedEnterpriseHosts { get; set; } = [];

    private static int? RemainingRateLimit(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
        && int.TryParse(values.FirstOrDefault(), out var remaining)
            ? remaining
            : null;

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "the run was cancelled or timed out",
        HttpRequestException http => http.Message,
        _ => "the response could not be read"
    };

    // ---- mapping ----

    private static GitHubCommit? MapCommit(JsonElement element)
    {
        var sha = Text(element, "sha");
        if (string.IsNullOrWhiteSpace(sha))
        {
            return null;
        }

        var commit = Child(element, "commit");
        var gitAuthor = Child(commit, "author");
        var gitCommitter = Child(commit, "committer");

        // GitHub gives both the git identity (name/email from the commit
        // object) and the linked account. Both are kept: the account is
        // the stable id, the git email is what a co-author trailer will
        // match on.
        var author = MapActor(Child(element, "author"), Text(gitAuthor, "name"), Text(gitAuthor, "email"));
        var committer = MapActor(Child(element, "committer"), Text(gitCommitter, "name"), Text(gitCommitter, "email"));

        // Anyone can write a commit naming a colleague's email as author.
        // Only a signature GitHub has verified says who actually made it.
        var verification = Child(commit, "verification");
        bool? verified = verification.ValueKind == JsonValueKind.Object
            && verification.TryGetProperty("verified", out var flag)
            && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? flag.GetBoolean()
                : null;

        return new GitHubCommit(
            sha!,
            Text(commit, "message"),
            Timestamp(gitAuthor, "date"),
            author,
            committer,
            Text(element, "html_url"),
            ChangedPaths(element),
            verified,
            Text(verification, "reason"));
    }

    private static GitHubPullRequest? MapPullRequest(JsonElement element, DateTime mergedAtUtc)
    {
        var id = Text(element, "id");
        if (string.IsNullOrWhiteSpace(id) || !element.TryGetProperty("number", out var number)
            || number.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return new GitHubPullRequest(
            id!,
            number.GetInt32(),
            Text(element, "title"),
            mergedAtUtc,
            MapActor(Child(element, "user")),
            Text(element, "html_url"),
            ChangedPaths(element));
    }

    private static GitHubActor? MapActor(JsonElement element, string? fallbackName = null, string? fallbackEmail = null)
    {
        var id = Text(element, "id");
        var login = Text(element, "login");

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(login)
            && string.IsNullOrWhiteSpace(fallbackName) && string.IsNullOrWhiteSpace(fallbackEmail))
        {
            return null;
        }

        return new GitHubActor(id, login, Text(element, "type"), fallbackName ?? Text(element, "name"), fallbackEmail);
    }

    /// <summary>
    /// File *paths* only — never the patch. The commits and pulls list
    /// endpoints omit files entirely, so this is usually empty and the
    /// language hints come from the detail endpoints a later iteration can
    /// add; an empty list is correctly reported as "no hints", not as
    /// "no languages".
    /// </summary>
    private static IReadOnlyList<string> ChangedPaths(JsonElement element)
    {
        if (!element.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var paths = new List<string>();
        foreach (var file in files.EnumerateArray().Take(MaxChangedPathsPerArtifact))
        {
            var path = Text(file, "filename");
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path!);
            }
        }

        return paths;
    }

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child)
            ? child
            : default;

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static DateTime? Timestamp(JsonElement element, string name)
    {
        var text = Text(element, name);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseCursor(string? cursor) =>
        DateTime.TryParse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
