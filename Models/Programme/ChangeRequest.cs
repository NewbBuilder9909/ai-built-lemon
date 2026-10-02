namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A scope-change record scoped to a Project, mirroring the LeaveRequest
/// submit -&gt; approve/reject shape already proven in Staff Ops. Exists so a
/// scope change has a request, a decision, and a "who decided" — ClickUp
/// re-sync edits to WorkItem fields happen invisibly otherwise.
/// </summary>
public sealed record ChangeRequest
{
    public required Guid ChangeRequestKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ProjectKey { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required ChangeRequestStatus Status { get; init; }

    public Guid? RequestedByStaffKey { get; init; }

    public Guid? DecidedByStaffKey { get; init; }

    public DateTime? DecidedAtUtc { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
