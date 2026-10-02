using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace ProgrammePulse.Services.Integrations.Tempo;

/// <summary>Read-only Tempo v4 worklog client. Pagination URLs are restricted to the configured Tempo API origin and endpoint.</summary>
public sealed class TempoApiClient(HttpClient client)
{
    private const int MaxPages = 1000;

    public async Task<IReadOnlyList<JsonElement>> GetWorklogsAsync(
        string accessToken, DateOnly? updatedFrom = null, CancellationToken cancellationToken = default)
        => await GetPagesAsync(accessToken, "/4/worklogs", "Tempo",
            updatedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);

    public Task<IReadOnlyList<JsonElement>> GetDeletedWorklogsAsync(
        string accessToken, DateOnly updatedFrom, CancellationToken cancellationToken = default) =>
        GetPagesAsync(accessToken, "/audit/1/events/deleted/types/worklog", "TempoAudit",
            updatedFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00", cancellationToken);

    private async Task<IReadOnlyList<JsonElement>> GetPagesAsync(string accessToken, string path,
        string provider, string? updatedFrom, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("A Tempo access token is required.", nameof(accessToken));
        }

        var worklogs = new List<JsonElement>();
        var first = new Uri(client.BaseAddress ?? throw new InvalidOperationException("Tempo base address is missing."),
            path + "?limit=1000" + (updatedFrom is null ? "" : "&updatedFrom=" + Uri.EscapeDataString(updatedFrom)));
        var next = first;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; page < MaxPages; page++)
        {
            if (!OutboundEndpointPolicy.IsAllowed(next, provider) || next.AbsolutePath != path
                || !SameWindow(next, updatedFrom) || !seen.Add(next.AbsoluteUri))
            {
                throw new JsonException("Tempo returned an unsafe or repeated pagination URL.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Tempo worklogs response has no results array.");
            }

            worklogs.AddRange(results.EnumerateArray().Select(item => item.Clone()));
            if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
                throw new JsonException("Tempo response has no pagination metadata.");
            string? nextUrl = null;
            if (metadata.TryGetProperty("next", out var nextValue) && nextValue.ValueKind != JsonValueKind.Null)
            {
                if (nextValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nextValue.GetString()))
                    throw new JsonException("Tempo returned invalid pagination metadata.");
                nextUrl = nextValue.GetString();
            }
            if (nextUrl is null && metadata.TryGetProperty("lastEvaluatedKey", out var key)
                && key.ValueKind != JsonValueKind.Null)
            {
                if (key.ValueKind != JsonValueKind.String) throw new JsonException("Tempo returned an invalid continuation key.");
                if (key.GetString() is { Length: > 0 } cursor)
                    nextUrl = QueryHelpers.AddQueryString(first.AbsoluteUri, "lastEvaluatedKey", cursor);
            }
            if (string.IsNullOrWhiteSpace(nextUrl))
            {
                return worklogs;
            }

            if (!Uri.TryCreate(first, nextUrl, out next)) throw new JsonException("Tempo returned an invalid pagination URL.");
        }

        throw new InvalidOperationException("Tempo worklogs exceeded the maximum page count.");
    }

    private static bool SameWindow(Uri page, string? updatedFrom)
    {
        var query = QueryHelpers.ParseQuery(page.Query);
        if (query.Any(pair => pair.Value.Count != 1 || pair.Key is not ("updatedFrom" or "limit" or "offset" or "lastEvaluatedKey"))) return false;
        if (updatedFrom is null) return !query.ContainsKey("updatedFrom");
        return query.TryGetValue("updatedFrom", out var value) && value.Count == 1 && value[0] == updatedFrom;
    }
}
