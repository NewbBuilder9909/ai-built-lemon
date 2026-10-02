namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Leaf unit of work. RawStatus keeps the source system's original status
/// text alongside the normalised Stage — same reason
/// Models/Erp/TransformedErpRecord keeps the raw status next to the
/// normalised one: it lets the overview surface "what did the source
/// actually say" without ever branching on it.
/// </summary>
public sealed record WorkItem
{
    public required Guid WorkItemKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid WorkstreamKey { get; init; }

    public required string Title { get; init; }

    public required WorkItemLifecycleStage Stage { get; init; }

    public string? RawStatus { get; init; }

    public bool IsMilestone { get; init; }

    /// <summary>
    /// The primary (first-resolved) assignee, derived from the full
    /// allocation set ClickUpMappingService.MapWorkItemAsync resolves and
    /// persists to WorkItemAllocation — not an independently tracked value.
    /// Kept (rather than requiring every caller to query allocations) for
    /// single-owner views that still need one, e.g. StaffPortalController's
    /// "My Work" — resource-capacity grouping uses the full allocation set
    /// instead, see ProgrammeOverviewQueryService.BuildResourceCapacity. Any
    /// future caller that sets this directly (bypassing
    /// ClickUpMappingService) would silently desync it from
    /// WorkItemAllocation — there's no enforcement of the relationship
    /// beyond this comment.
    /// </summary>
    public Guid? AssignedStaffKey { get; init; }

    public DateTime? DueDateUtc { get; init; }

    public decimal? EstimatedHours { get; init; }

    public string? ExternalSource { get; init; }

    public string? ExternalId { get; init; }

    public string? ParentExternalId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
