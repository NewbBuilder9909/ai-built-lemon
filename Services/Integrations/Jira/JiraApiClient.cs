using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProgrammePulse.Services.Integrations.Jira;

public sealed record JiraIssuePage(IReadOnlyList<JsonElement> Issues, string? NextPageToken);

/// <summary>Read-only Jira Cloud v3 search. Authentication is supplied per request so tenant tokens never enter pooled HttpClient defaults.</summary>
public sealed class JiraApiClient(HttpClient client)
{
    private const int MaxPages = 1000;

    public async Task<IReadOnlyList<JsonElement>> GetIssuesAsync(
        Guid cloudId, IReadOnlyList<long> projectIds, string accessToken, CancellationToken cancellationToken = default)
    {
        if (cloudId == Guid.Empty || projectIds.Count == 0 || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("A Jira cloud ID, selected projects and access token are required.");
        }

        var issues = new List<JsonElement>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? nextPageToken = null;
        var jql = $"project in ({string.Join(",", projectIds.Distinct())}) ORDER BY updated ASC";

        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"ex/jira/{cloudId:D}/rest/api/3/search/jql";
            var destination = new Uri(client.BaseAddress ?? throw new InvalidOperationException("Missing Jira base address."), path);
            OutboundEndpointPolicy.RequireAllowed(destination, "Jira");
            using var request = new HttpRequestMessage(HttpMethod.Post, destination);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new
            {
                jql,
                fields = new[] { "summary", "status", "project", "issuetype", "assignee", "duedate", "timeoriginalestimate", "updated", "parent" },
                maxResults = 100,
                nextPageToken
            });

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("issues", out var pageIssues) || pageIssues.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Jira search response has no issues array.");
            }

            issues.AddRange(pageIssues.EnumerateArray().Select(issue => issue.Clone()));
            nextPageToken = root.TryGetProperty("nextPageToken", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString() : null;
            if (string.IsNullOrEmpty(nextPageToken))
            {
                return issues;
            }

            if (!seenTokens.Add(nextPageToken))
            {
                throw new JsonException("Jira repeated a pagination token.");
            }
        }

        throw new InvalidOperationException("Jira search exceeded the maximum page count.");
    }
}
