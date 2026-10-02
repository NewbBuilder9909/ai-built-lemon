using ProgrammePulse.Models.Integrations.ClickUp.Raw;
using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Services.Integrations.ClickUp;

public sealed class ClickUpMappingService(
    IProgrammeRepository programmeRepository,
    IStaffIdentityResolver identityResolver,
    IClickUpStatusMapper statusMapper,
    TimeProvider timeProvider) : IClickUpMappingService
{
    private const string Source = "ClickUp";
    private const string AssigneeContext = "task assignee";
    private const string TimeEntryUserContext = "time entry user";

    public Task<Programme> MapProgrammeAsync(ClickUpSpaceDto space, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertProgrammeAsync(new Programme
        {
            ProgrammeKey = Guid.NewGuid(),
            Name = space.Name,
            ExternalSource = Source,
            ExternalId = space.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    public Task<Project> MapProjectAsync(ClickUpFolderDto folder, Guid programmeKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertProjectAsync(new Project
        {
            ProjectKey = Guid.NewGuid(),
            ProgrammeKey = programmeKey,
            Name = folder.Name,
            ExternalSource = Source,
            ExternalId = folder.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    public Task<Workstream> MapWorkstreamAsync(ClickUpListDto list, Guid projectKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return programmeRepository.UpsertWorkstreamAsync(new Workstream
        {
            WorkstreamKey = Guid.NewGuid(),
            ProjectKey = projectKey,
            Name = list.Name,
            ExternalSource = Source,
            ExternalId = list.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, tenantId);
    }

    public async Task<WorkItem> MapWorkItemAsync(ClickUpTaskDto task, Guid workstreamKey, Guid tenantId) =>
        (await MapWorkItemsAsync([task], workstreamKey, tenantId))[0];

    /// <summary>
    /// Maps a list's tasks and writes them, with their allocations, in one
    /// batched upsert rather than several round trips per task.
    /// </summary>
    public async Task<IReadOnlyList<WorkItem>> MapWorkItemsAsync(IReadOnlyList<ClickUpTaskDto> tasks, Guid workstreamKey, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var upserts = new List<WorkItemUpsert>(tasks.Count);
        foreach (var task in tasks)
        {
            var assigneeStaffKeys = await ResolveAssigneeStaffKeysAsync(task, tenantId);
            upserts.Add(new WorkItemUpsert(ToWorkItem(task, workstreamKey, assigneeStaffKeys, now), assigneeStaffKeys));
        }

        return await programmeRepository.UpsertWorkItemsAsync(upserts, now, tenantId);
    }

    private WorkItem ToWorkItem(ClickUpTaskDto task, Guid workstreamKey, IReadOnlyList<Guid> assigneeStaffKeys, DateTime now)
    {
        var stage = statusMapper.Map(task.Status?.Status);
        return new WorkItem
        {
            WorkItemKey = Guid.NewGuid(),
            WorkstreamKey = workstreamKey,
            Title = task.Name,
            Stage = stage,
            RawStatus = task.Status?.Status,
            IsMilestone = IsFlaggedAsMilestone(task),
            // The primary/first resolved assignee — derived from the same
            // set the batched upsert persists as allocations in full, not
            // independently resolved. See WorkItem.AssignedStaffKey's doc
            // comment for why this field still exists.
            AssignedStaffKey = assigneeStaffKeys.Count > 0 ? assigneeStaffKeys[0] : null,
            DueDateUtc = ParseUnixMs(task.DateDue),
            EstimatedHours = task.TimeEstimateMs.HasValue ? task.TimeEstimateMs.Value / 3_600_000m : null,
            ExternalSource = Source,
            ExternalId = task.Id,
            ParentExternalId = task.Parent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Every assignee goes through IStaffIdentityResolver (explicit link by
    /// ClickUp user id, then by email; a roster email match is only a
    /// suggestion). One that still can't be matched is recorded on the identity queue by the
    /// resolver — it is not silently dropped the way the original
    /// email-only join did.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveAssigneeStaffKeysAsync(ClickUpTaskDto task, Guid tenantId)
    {
        var resolved = new List<Guid>();
        foreach (var assignee in task.Assignees)
        {
            var staffKey = await identityResolver.ResolveAsync(Source, assignee.Id.ToString(), assignee.Email, assignee.Username, AssigneeContext, tenantId);
            if (staffKey is not null && !resolved.Contains(staffKey.Value))
            {
                resolved.Add(staffKey.Value);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Correlates to a WorkItem via the entry's task id (null if that task
    /// hasn't been synced) and to a StaffProfile via the same identity
    /// resolver used for task assignees. Entries ClickUp returns without a
    /// "start" timestamp (manually-created entries can omit it) map with
    /// StartedAtUtc = null — period-based reporting excludes those rather
    /// than guessing a date.
    /// </summary>
    public async Task<TimeEntry> MapTimeEntryAsync(ClickUpTimeEntryDto entry, Guid tenantId) =>
        (await MapTimeEntriesAsync([entry], tenantId))[0];

    /// <summary>
    /// The batched form: one read for every referenced task's work item key,
    /// then one batched upsert, instead of several round trips per entry.
    /// </summary>
    public async Task<IReadOnlyList<TimeEntry>> MapTimeEntriesAsync(IReadOnlyList<ClickUpTimeEntryDto> entries, Guid tenantId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var workItemKeys = await programmeRepository.GetWorkItemKeysByExternalIdAsync(
            Source, entries.Where(e => e.Task is not null).Select(e => e.Task!.Id).ToList(), tenantId);

        var mapped = new List<TimeEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var staffKey = entry.User is null
                ? null
                : await identityResolver.ResolveAsync(Source, entry.User.Id.ToString(), entry.User.Email, entry.User.Username, TimeEntryUserContext, tenantId);

            mapped.Add(new TimeEntry
            {
                TimeEntryKey = Guid.NewGuid(),
                WorkItemKey = entry.Task is not null && workItemKeys.TryGetValue(entry.Task.Id, out var key) ? key : null,
                StaffKey = staffKey,
                DurationHours = entry.DurationMs / 3_600_000m,
                StartedAtUtc = ParseUnixMs(entry.StartMs),
                IsBillable = entry.Billable,
                ExternalSource = Source,
                ExternalId = entry.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        return await programmeRepository.UpsertTimeEntriesAsync(mapped, tenantId);
    }

    /// <summary>
    /// ClickUp has no built-in "milestone" concept, so this looks for a
    /// custom field literally named "Milestone" with a truthy value — the
    /// convention a customer's spaces would need to adopt for milestones to
    /// surface on the overview. No such field present means "not a
    /// milestone", not "unknown".
    /// </summary>
    private static bool IsFlaggedAsMilestone(ClickUpTaskDto task)
    {
        var field = task.CustomFields.FirstOrDefault(f => string.Equals(f.Name, "Milestone", StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return false;
        }

        return field.Value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.Number => field.Value.GetDouble() != 0,
            _ => false
        };
    }

    private static DateTime? ParseUnixMs(string? ms)
    {
        if (!long.TryParse(ms, out var parsed))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(parsed).UtcDateTime;
    }
}
