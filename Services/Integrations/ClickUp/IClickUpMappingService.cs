using ProgrammePulse.Models.Integrations.ClickUp.Raw;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// The one place Bronze DTOs and Silver models both appear. Silver/Gold code
/// never depends on this — only ClickUpSyncService calls it.
/// </summary>
public interface IClickUpMappingService
{
    Task<Programme> MapProgrammeAsync(ClickUpSpaceDto space, Guid tenantId);

    Task<Project> MapProjectAsync(ClickUpFolderDto folder, Guid programmeKey, Guid tenantId);

    Task<Workstream> MapWorkstreamAsync(ClickUpListDto list, Guid projectKey, Guid tenantId);

    Task<WorkItem> MapWorkItemAsync(ClickUpTaskDto task, Guid workstreamKey, Guid tenantId);

    Task<TimeEntry> MapTimeEntryAsync(ClickUpTimeEntryDto entry, Guid tenantId);

    /// <summary>A list's tasks in one batched write. The default maps one at a time.</summary>
    async Task<IReadOnlyList<WorkItem>> MapWorkItemsAsync(IReadOnlyList<ClickUpTaskDto> tasks, Guid workstreamKey, Guid tenantId)
    {
        var mapped = new List<WorkItem>(tasks.Count);
        foreach (var task in tasks)
        {
            mapped.Add(await MapWorkItemAsync(task, workstreamKey, tenantId));
        }

        return mapped;
    }

    /// <summary>Time entries in one batched write. The default maps one at a time.</summary>
    async Task<IReadOnlyList<TimeEntry>> MapTimeEntriesAsync(IReadOnlyList<ClickUpTimeEntryDto> entries, Guid tenantId)
    {
        var mapped = new List<TimeEntry>(entries.Count);
        foreach (var entry in entries)
        {
            mapped.Add(await MapTimeEntryAsync(entry, tenantId));
        }

        return mapped;
    }
}
