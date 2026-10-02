namespace ProgrammePulse.Models.Programme;

/// <summary>
/// One row per ClickUp assignee on a WorkItem — a task can carry multiple
/// assignees, which WorkItem.AssignedStaffKey alone can't represent (see its
/// doc comment). IsPrimary marks the one assignee AssignedStaffKey is
/// derived from (the first resolved assignee); every other row is an
/// additional co-assignee.
/// </summary>
public sealed record WorkItemAllocation
{
    public required Guid AllocationKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid WorkItemKey { get; init; }

    public required Guid StaffKey { get; init; }

    public required bool IsPrimary { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
