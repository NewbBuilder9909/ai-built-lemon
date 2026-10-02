using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Models.Integrations.ClickUp.Raw;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// Bronze-layer HTTP boundary — the only place in the app allowed to know
/// ClickUp's API shape or call clickup.com. Every method returns raw JSON
/// alongside the parsed DTO (see RawEntity) so the caller can persist an
/// untouched capture before any mapping happens.
/// </summary>
public interface IClickUpApiClient
{
    /// <summary>Returns an immutable credential-bound client without modifying shared HTTP defaults.</summary>
    IClickUpApiClient WithCredential(string apiToken);

    Task<IReadOnlyList<RawEntity<ClickUpUserDto>>> GetWorkspaceMembersAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<ClickUpSpaceDto>>> GetSpacesAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<ClickUpFolderDto>>> GetFoldersAsync(string spaceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetListsAsync(string folderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<ClickUpListDto>>> GetFolderlessListsAsync(string spaceId, CancellationToken cancellationToken = default);

    /// <summary>Includes subtasks (via Parent) and custom fields — both come back inline on each task.</summary>
    Task<IReadOnlyList<RawEntity<ClickUpTaskDto>>> GetTasksAsync(string listId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<ClickUpTimeEntryDto>>> GetTimeEntriesAsync(string workspaceId, CancellationToken cancellationToken = default);
}
