using ProgrammePulse.Services.Integrations.Abstractions;
using System.Text.Json;
using ProgrammePulse.Models.Integrations.ClickUp.Raw;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// Thin wrapper over ClickUp's REST API (https://developer.clickup.com/reference).
/// Credentials are bound to immutable clients and sent on each request.
/// Shared HttpClient defaults never contain tenant credentials.
/// </summary>
public sealed class ClickUpApiClient(HttpClient httpClient) : IClickUpApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string? credential;

    private ClickUpApiClient(HttpClient client, string token) : this(client) => credential = token;

    public IClickUpApiClient WithCredential(string apiToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        return new ClickUpApiClient(httpClient, apiToken);
    }
    public async Task<IReadOnlyList<RawEntity<ClickUpUserDto>>> GetWorkspaceMembersAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("team", cancellationToken);
        if (!document.RootElement.TryGetProperty("teams", out var teams))
        {
            return [];
        }

        foreach (var team in teams.EnumerateArray())
        {
            if (team.TryGetProperty("id", out var idProp) && idProp.GetString() == workspaceId &&
                team.TryGetProperty("members", out var members))
            {
                return members.EnumerateArray()
                    .Select(m => m.TryGetProperty("user", out var user) ? user : m)
                    .Select(ToRawEntity<ClickUpUserDto>)
                    .ToList();
            }
        }

        return [];
    }

    public Task<IReadOnlyList<RawEntity<ClickUpSpaceDto>>> GetSpacesAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpSpaceDto>($"team/{Uri.EscapeDataString(workspaceId)}/space?archived=false", "spaces", cancellationToken);

    public Task<IReadOnlyList<RawEntity<ClickUpFolderDto>>> GetFoldersAsync(string spaceId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpFolderDto>($"space/{Uri.EscapeDataString(spaceId)}/folder?archived=false", "folders", cancellationToken);

    public Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetListsAsync(string folderId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpListDto>($"folder/{Uri.EscapeDataString(folderId)}/list?archived=false", "lists", cancellationToken);

    public Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetFolderlessListsAsync(string spaceId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpListDto>($"space/{Uri.EscapeDataString(spaceId)}/list?archived=false", "lists", cancellationToken);

    public Task<IReadOnlyList<RawEntity<ClickUpTaskDto>>> GetTasksAsync(string listId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpTaskDto>($"list/{Uri.EscapeDataString(listId)}/task?subtasks=true&include_closed=true", "tasks", cancellationToken);

    public Task<IReadOnlyList<RawEntity<ClickUpTimeEntryDto>>> GetTimeEntriesAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        FetchArrayAsync<ClickUpTimeEntryDto>($"team/{Uri.EscapeDataString(workspaceId)}/time_entries", "data", cancellationToken);

    private async Task<IReadOnlyList<RawEntity<T>>> FetchArrayAsync<T>(string relativePath, string arrayPropertyName, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(relativePath, cancellationToken);
        if (!document.RootElement.TryGetProperty(arrayPropertyName, out var array))
        {
            return [];
        }

        return array.EnumerateArray().Select(ToRawEntity<T>).ToList();
    }

    private async Task<JsonDocument> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        var destination = new Uri(httpClient.BaseAddress ?? throw new InvalidOperationException("Missing API base address."), relativePath);
        ProgrammePulse.Services.Integrations.OutboundEndpointPolicy.RequireAllowed(destination, "ClickUp");
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
