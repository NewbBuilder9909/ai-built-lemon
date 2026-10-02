namespace ProgrammePulse.Models.Programme;

/// <summary>
/// "WorkItemKey is blocked by DependsOnWorkItemKey." One row per dependency
/// edge; the overview's blocked-count reads WorkItem.Stage directly, this
/// table is what lets a future detail view explain *why*.
/// </summary>
public sealed record Dependency
{
    public required Guid DependencyKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid WorkItemKey { get; init; }

    public required Guid DependsOnWorkItemKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}
