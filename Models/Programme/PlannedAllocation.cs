namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A planned block of a person's time against a Project, mapped from a
/// scheduling source (Hub Planner bookings today). Deliberately a separate
/// Silver entity from <see cref="WorkItem"/>: a booking is intent to spend
/// time, not evidence that work was delivered, so nothing here carries a
/// <see cref="WorkItemLifecycleStage"/> and no Gold consumer may count a
/// planned allocation toward "done" or "% complete". Before this entity
/// existed, elapsed bookings were mapped to Done work items and time
/// passing looked like delivery progress — the defect the GTM review
/// (docs/archive/platform-mapper-gtm-review-2026-09-16.md, B04) called out.
///
/// AllocatedHours is the source's own number when its unit is hours, never
/// a derived figure — Hub Planner's per-day vs. per-booking semantics are
/// still unverified against a live account, so Gold labels it "as recorded
/// by the source" and reports how many allocations carry no hours at all
/// rather than treating missing as zero.
/// </summary>
public sealed record PlannedAllocation
{
    public required Guid PlannedAllocationKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ProjectKey { get; init; }

    /// <summary>Null when the source's person could not be matched to a StaffProfile — see <see cref="UnresolvedIdentity"/>.</summary>
    public Guid? StaffKey { get; init; }

    public required string Title { get; init; }

    public DateTime? StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public decimal? AllocatedHours { get; init; }

    public decimal? AllocationPercent { get; init; }

    /// <summary>The source's own type/status text (e.g. Hub Planner "SCHEDULED"), kept for display and diagnostics only.</summary>
    public string? RawType { get; init; }

    public string? ExternalSource { get; init; }

    public string? ExternalId { get; init; }

    /// <summary>The source's person identifier, so an unresolved allocation can be traced back once an identity link is created.</summary>
    public string? ExternalResourceId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
