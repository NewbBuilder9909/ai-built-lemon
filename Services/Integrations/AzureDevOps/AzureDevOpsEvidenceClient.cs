using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProgrammePulse.Models.Integrations.AzureDevOps.Raw;

namespace ProgrammePulse.Services.Integrations.AzureDevOps;

/// <summary>
/// Read-only Azure DevOps Services REST client (api-version 7.1).
/// Authentication is a personal access token supplied per request, never
/// set on pooled HttpClient defaults — several tenants share this client.
///
/// Azure DevOps does not always say "unauthorised" with a 401. Depending
/// on the route a refused token produces a 203 carrying an HTML sign-in
/// page, or a redirect to one. The registered handler does not follow
/// redirects (OutboundEndpointPolicy.CreateHandler), so all three are
/// visible here and all three are treated as a refused token — reading
/// the sign-in page as an empty JSON result is exactly the "we looked and
/// found nothing" mistake the coverage model exists to prevent.
///
/// Like the GitHub client, this never fetches diffs, file contents or
/// commit bodies beyond the subject line the mapper keeps, and a failed
/// page returns <see cref="AzureDevOpsPage{T}.IsComplete"/> false with a
/// reason instead of giving up quietly.
/// </summary>
public sealed class AzureDevOpsEvidenceClient(HttpClient client) : IAzureDevOpsEvidenceClient
{
    private const int PageSize = 100;
    private const int MaxPagesPerStream = 50;
    private const string ApiVersion = "api-version=7.1";

    private enum Outcome
    {
        Ok,
        TokenRejected,
        NotFound
    }

    public async Task<AzureDevOpsVerification> VerifyOrganisationAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        var (outcome, document) = await SendAsync(apiBaseUrl, $"_apis/projects?$top=1&{ApiVersion}", accessToken, cancellationToken);
        document?.Dispose();

        return outcome switch
        {
            Outcome.Ok => AzureDevOpsVerification.Valid,
            Outcome.NotFound => AzureDevOpsVerification.OrganisationNotFound,
            _ => AzureDevOpsVerification.TokenRejected
        };
    }

    public async Task<IReadOnlyList<AzureDevOpsRepository>> GetAccessibleRepositoriesAsync(
        string apiBaseUrl, string accessToken, CancellationToken cancellationToken)
    {
        var (outcome, document) = await SendAsync(apiBaseUrl, $"_apis/git/repositories?{ApiVersion}", accessToken, cancellationToken);
        if (outcome != Outcome.Ok || document is null)
        {
            return [];
        }

        using (document)
        {
            var repositories = new List<AzureDevOpsRepository>();
            foreach (var element in Values(document.RootElement))
            {
                var repository = MapRepository(element);

                // Disabled and empty repositories have nothing to read; a
                // key that fails the name policy cannot be requested safely.
                if (repository is { IsDisabled: false, DefaultBranch: not null }
                    && AzureDevOpsHostPolicy.IsValidRepositoryKey(repository.Key))
                {
                    repositories.Add(repository);
                }
            }

            return repositories;
        }
    }

    public async Task<AzureDevOpsPage<AzureDevOpsCommit>> GetDefaultBranchCommitsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        var commits = new List<AzureDevOpsCommit>();
        AzureDevOpsRepository repository;
        try
        {
            repository = await GetRepositoryAsync(apiBaseUrl, accessToken, repositoryKey, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new AzureDevOpsPage<AzureDevOpsCommit>(commits, null, false, Describe(ex));
        }

        if (repository.DefaultBranch is null)
        {
            // An empty repository: complete, and genuinely nothing to see.
            return new AzureDevOpsPage<AzureDevOpsCommit>(commits, cursor, true);
        }

        // The cursor is an ISO timestamp of the newest commit seen.
        // Overlapping by a minute costs nothing — the upsert is idempotent —
        // and guards against commits that share the boundary second.
        var since = ParseCursor(cursor)?.AddMinutes(-1);
        var branch = repository.DefaultBranch.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? repository.DefaultBranch["refs/heads/".Length..]
            : repository.DefaultBranch;

        var query = new StringBuilder()
            .Append("searchCriteria.itemVersion.version=").Append(Uri.EscapeDataString(branch))
            .Append("&searchCriteria.itemVersion.versionType=branch")
            .Append("&searchCriteria.$top=").Append(PageSize);
        if (since is not null)
        {
            query.Append("&searchCriteria.fromDate=").Append(Uri.EscapeDataString(Iso(since.Value)));
        }

        DateTime? newest = since;

        for (var page = 0; page < MaxPagesPerStream; page++)
        {
            JsonDocument document;
            try
            {
                document = await GetRepositoryResourceAsync(
                    apiBaseUrl, accessToken, repositoryKey,
                    $"commits?{query}&searchCriteria.$skip={page * PageSize}&{ApiVersion}", cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Everything fetched so far is kept; the cursor is not
                // advanced past it, so the next run re-reads from here.
                return new AzureDevOpsPage<AzureDevOpsCommit>(commits, null, false, Describe(ex));
            }

            using (document)
            {
                var count = 0;
                foreach (var element in Values(document.RootElement))
                {
                    count++;
                    var commit = MapCommit(element, apiBaseUrl, repositoryKey);
                    if (commit is null)
                    {
                        continue;
                    }

                    commits.Add(commit);
                    var stamp = commit.CommittedAtUtc ?? commit.AuthoredAtUtc;
                    if (stamp is not null && (newest is null || stamp > newest))
                    {
                        newest = stamp;
                    }
                }

                if (count < PageSize)
                {
                    return new AzureDevOpsPage<AzureDevOpsCommit>(commits, newest is null ? cursor : Iso(newest.Value), true);
                }
            }
        }

        // Hit the page ceiling: more remains, and saying so is the honest answer.
        return new AzureDevOpsPage<AzureDevOpsCommit>(
            commits, newest is null ? cursor : Iso(newest.Value), false, "more history remains beyond this run's page limit");
    }

    public async Task<AzureDevOpsPage<AzureDevOpsPullRequest>> GetCompletedPullRequestsAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? cursor, CancellationToken cancellationToken)
    {
        var pullRequests = new List<AzureDevOpsPullRequest>();
        var since = ParseCursor(cursor)?.AddMinutes(-1);

        var query = new StringBuilder("searchCriteria.status=completed&$top=").Append(PageSize);
        if (since is not null)
        {
            query.Append("&searchCriteria.queryTimeRangeType=closed&searchCriteria.minTime=")
                .Append(Uri.EscapeDataString(Iso(since.Value)));
        }

        DateTime? newest = since;

        for (var page = 0; page < MaxPagesPerStream; page++)
        {
            JsonDocument document;
            try
            {
                document = await GetRepositoryResourceAsync(
                    apiBaseUrl, accessToken, repositoryKey,
                    $"pullrequests?{query}&$skip={page * PageSize}&{ApiVersion}", cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                return new AzureDevOpsPage<AzureDevOpsPullRequest>(pullRequests, null, false, Describe(ex));
            }

            using (document)
            {
                var count = 0;
                foreach (var element in Values(document.RootElement))
                {
                    count++;
                    var pullRequest = MapPullRequest(element, apiBaseUrl, repositoryKey);

                    // The time filter is applied server-side too; this is
                    // the belt to that brace, so a server that ignores the
                    // parameter cannot turn every run into a full replay.
                    if (pullRequest?.ClosedAtUtc is not { } closed || (since is not null && closed < since))
                    {
                        continue;
                    }

                    pullRequests.Add(pullRequest);
                    if (newest is null || closed > newest)
                    {
                        newest = closed;
                    }
                }

                if (count < PageSize)
                {
                    return new AzureDevOpsPage<AzureDevOpsPullRequest>(pullRequests, newest is null ? cursor : Iso(newest.Value), true);
                }
            }
        }

        return new AzureDevOpsPage<AzureDevOpsPullRequest>(
            pullRequests, newest is null ? cursor : Iso(newest.Value), false, "more pull requests remain beyond this run's page limit");
    }

    // ---- HTTP ----

    private async Task<AzureDevOpsRepository> GetRepositoryAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, CancellationToken cancellationToken)
    {
        using var document = await GetRepositoryResourceAsync(apiBaseUrl, accessToken, repositoryKey, relative: null, cancellationToken);
        var repository = MapRepository(document.RootElement)
            ?? throw new JsonException("unexpected repository response shape");

        if (repository.IsDisabled)
        {
            throw new AzureDevOpsAccessLostException(repositoryKey, "the repository is disabled");
        }

        return repository;
    }

    /// <summary>
    /// A resource under one repository. A refused token or a missing
    /// repository throws <see cref="AzureDevOpsAccessLostException"/>; a
    /// transport or throttling failure throws HttpRequestException, which
    /// callers record as partial.
    /// </summary>
    private async Task<JsonDocument> GetRepositoryResourceAsync(
        string apiBaseUrl, string accessToken, string repositoryKey, string? relative, CancellationToken cancellationToken)
    {
        if (!AzureDevOpsHostPolicy.IsValidRepositoryKey(repositoryKey))
        {
            throw new AzureDevOpsAccessLostException(repositoryKey, "the repository name is not one this connector can request");
        }

        var (project, name) = Split(repositoryKey);
        var path = $"{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(name)}"
            + (relative is null ? $"?{ApiVersion}" : "/" + relative);

        var (outcome, document) = await SendAsync(apiBaseUrl, path, accessToken, cancellationToken);
        return outcome switch
        {
            Outcome.Ok when document is not null => document,
            Outcome.NotFound => throw new AzureDevOpsAccessLostException(repositoryKey, "the repository was not found or is no longer visible"),
            _ => throw new AzureDevOpsAccessLostException(repositoryKey, "the access token was refused")
        };
    }

    private async Task<(Outcome Outcome, JsonDocument? Document)> SendAsync(
        string apiBaseUrl, string relativePath, string accessToken, CancellationToken cancellationToken)
    {
        // Re-validated on every request, so no caller — and no edited
        // database row — can move a token off dev.azure.com.
        var canonical = AzureDevOpsHostPolicy.Canonicalize(apiBaseUrl)
            ?? throw new InvalidOperationException("The evidence connection's API host is not permitted.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{canonical}/{relativePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + accessToken)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ProgrammePulse-Evidence");

        using var response = await client.SendAsync(request, cancellationToken);

        var status = (int)response.StatusCode;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation
            || status is >= 300 and < 400)
        {
            return (Outcome.TokenRejected, null);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return (Outcome.NotFound, null);
        }

        // Throttling (429) and server errors reach here only after
        // TransientHttpRetryHandler has given up; the caller records them
        // as partial coverage.
        response.EnsureSuccessStatusCode();

        // A 200 carrying HTML is a sign-in page served without a redirect.
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return (Outcome.TokenRejected, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (Outcome.Ok, await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken));
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "the run was cancelled or timed out",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Azure DevOps rate limited the run",
        HttpRequestException http => http.Message,
        _ => "the response could not be read"
    };

    // ---- mapping ----

    private static AzureDevOpsRepository? MapRepository(JsonElement element)
    {
        var id = Text(element, "id");
        var name = Text(element, "name");
        var project = Text(Child(element, "project"), "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(project))
        {
            return null;
        }

        var disabled = element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("isDisabled", out var flag)
            && flag.ValueKind == JsonValueKind.True;

        var defaultBranch = Text(element, "defaultBranch");
        return new AzureDevOpsRepository(id!, project!, name!, string.IsNullOrWhiteSpace(defaultBranch) ? null : defaultBranch, disabled);
    }

    private static AzureDevOpsCommit? MapCommit(JsonElement element, string apiBaseUrl, string repositoryKey)
    {
        var commitId = Text(element, "commitId");
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        var author = Child(element, "author");
        var committer = Child(element, "committer");

        return new AzureDevOpsCommit(
            commitId!,
            Text(element, "comment"),
            Timestamp(author, "date"),
            Timestamp(committer, "date"),
            GitIdentity(author),
            GitIdentity(committer),
            WebUrl(apiBaseUrl, repositoryKey, $"commit/{Uri.EscapeDataString(commitId!)}"));
    }

    private static AzureDevOpsPullRequest? MapPullRequest(JsonElement element, string apiBaseUrl, string repositoryKey)
    {
        if (!element.TryGetProperty("pullRequestId", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var id = idElement.GetInt32();
        var reviewers = new List<AzureDevOpsReviewer>();

        if (element.TryGetProperty("reviewers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var reviewer in list.EnumerateArray())
            {
                var identity = AccountIdentity(reviewer);
                if (identity is null)
                {
                    continue;
                }

                var vote = reviewer.TryGetProperty("vote", out var voteElement) && voteElement.ValueKind == JsonValueKind.Number
                    ? voteElement.GetInt32()
                    : 0;
                reviewers.Add(new AzureDevOpsReviewer(identity, vote));
            }
        }

        return new AzureDevOpsPullRequest(
            id,
            Text(element, "title"),
            Timestamp(element, "closedDate"),
            AccountIdentity(Child(element, "createdBy")),
            reviewers,
            WebUrl(apiBaseUrl, repositoryKey, $"pullrequest/{id.ToString(CultureInfo.InvariantCulture)}"));
    }

    /// <summary>A git identity: name and email only. Azure DevOps links no account to a commit.</summary>
    private static AzureDevOpsIdentity? GitIdentity(JsonElement element)
    {
        var name = Text(element, "name");
        var email = Text(element, "email");
        return string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email)
            ? null
            : new AzureDevOpsIdentity(Id: null, DisplayName: name, UniqueName: null, Email: email);
    }

    /// <summary>
    /// An account identity: a stable id, plus the unique name — usually the
    /// sign-in address, which is offered to the queue as an email
    /// *suggestion* and never maps anyone on its own.
    /// </summary>
    private static AzureDevOpsIdentity? AccountIdentity(JsonElement element)
    {
        var id = Text(element, "id");
        var uniqueName = Text(element, "uniqueName");
        var displayName = Text(element, "displayName");
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(uniqueName))
        {
            return null;
        }

        var isContainer = element.TryGetProperty("isContainer", out var container) && container.ValueKind == JsonValueKind.True;
        var email = uniqueName is not null && uniqueName.Contains('@') && !uniqueName.Contains('\\') ? uniqueName : null;

        return new AzureDevOpsIdentity(id, displayName, uniqueName, email, isContainer);
    }

    /// <summary>Built from the validated base rather than read from the payload, so a stored link can only point at dev.azure.com.</summary>
    private static string? WebUrl(string apiBaseUrl, string repositoryKey, string tail)
    {
        var canonical = AzureDevOpsHostPolicy.Canonicalize(apiBaseUrl);
        if (canonical is null)
        {
            return null;
        }

        var (project, name) = Split(repositoryKey);
        return $"{canonical}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(name)}/{tail}";
    }

    private static (string Project, string Name) Split(string repositoryKey)
    {
        var slash = repositoryKey.IndexOf('/');
        return (repositoryKey[..slash], repositoryKey[(slash + 1)..]);
    }

    private static IEnumerable<JsonElement> Values(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw new JsonException("unexpected response shape");

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

    private static DateTime? Timestamp(JsonElement element, string name) => ParseCursor(Text(element, name));

    private static DateTime? ParseCursor(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
