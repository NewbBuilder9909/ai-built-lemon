using ProgrammePulse.Services.Integrations.Abstractions;
using System.Text.Json;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// Thin wrapper over Hub Planner's REST API
/// (https://github.com/hubplanner/API). The HttpClient is registered as a
/// typed client with immutable credentials sent on each request.
/// Shared HttpClient defaults never contain tenant credentials.
///
/// Rate limits (documented: 6000 calls/day, 50 calls per 5 seconds) are
/// handled by the shared TransientHttpRetryHandler in the client pipeline,
/// which honours Retry-After on a 429 and backs off on 5xx — this class
/// no longer carries its own one-shot retry.
///
/// Assumes each list endpoint returns a bare JSON array (not wrapped in a
/// named property the way ClickUp wraps "spaces"/"folders"/etc.) — Hub
/// Planner's public docs describe the page/limit query parameters but don't
/// show a full example response envelope. If a real account's response
/// turns out to be wrapped, FetchAllPagesAsync's array check below will
/// return an empty list instead of throwing — worth confirming against a
/// live account before this is used for a real sync.
/// </summary>
public sealed class HubPlannerApiClient(HttpClient httpClient) : IHubPlannerApiClient
{
    private const int PageSize = 1000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string? credential;

    private HubPlannerApiClient(HttpClient client, string token) : this(client) => credential = token;

    public IHubPlannerApiClient WithCredential(string apiToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        return new HubPlannerApiClient(httpClient, apiToken);
    }
    public Task<IReadOnlyList<RawEntity<HubPlannerProjectDto>>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        FetchAllPagesAsync<HubPlannerProjectDto>("project", cancellationToken);

    public Task<IReadOnlyList<RawEntity<HubPlannerResourceDto>>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
        FetchAllPagesAsync<HubPlannerResourceDto>("resource", cancellationToken);

    public Task<IReadOnlyList<RawEntity<HubPlannerBookingDto>>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
        FetchAllPagesAsync<HubPlannerBookingDto>("booking", cancellationToken);

    public Task<IReadOnlyList<RawEntity<HubPlannerClientDto>>> GetClientsAsync(CancellationToken cancellationToken = default) =>
        FetchAllPagesAsync<HubPlannerClientDto>("client", cancellationToken);

    private async Task<IReadOnlyList<RawEntity<T>>> FetchAllPagesAsync<T>(string resourcePath, CancellationToken cancellationToken)
    {
        var results = new List<RawEntity<T>>();
        var page = 0;

        while (true)
        {
            using var document = await GetJsonAsync($"{resourcePath}?page={page}&limit={PageSize}", cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageCount = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                results.Add(ToRawEntity<T>(element));
                pageCount++;
            }

            if (pageCount < PageSize)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private async Task<JsonDocument> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        var destination = new Uri(httpClient.BaseAddress ?? throw new InvalidOperationException("Missing API base address."), relativePath);
        ProgrammePulse.Services.Integrations.OutboundEndpointPolicy.RequireAllowed(destination, "HubPlanner");
        using var request = new HttpRequestMessage(HttpMethod.Get, destination);
        request.Headers.Add("Authorization", credential);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static RawEntity<T> ToRawEntity<T>(JsonElement element) =>
        new(element.Deserialize<T>(SerializerOptions)!, element.GetRawText());
}
