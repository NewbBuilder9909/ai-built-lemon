namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// UnresolvedResources is the number of distinct Hub Planner resources this
/// run could not match to a StaffProfile (now on the identity queue);
/// SkippedBookings are bookings against a project the run didn't see.
/// </summary>
public sealed record HubPlannerSyncResult(int Projects, int PlannedAllocations, int UnresolvedResources, int SkippedBookings, int AmbiguousResources = 0, int UnidentifiableSightings = 0)
{
    public string Describe() =>
        $"{Projects} projects, {PlannedAllocations} bookings as planned allocations"
        + (UnresolvedResources > 0 ? $", {UnresolvedResources} unmatched people" + (AmbiguousResources > 0 ? $" ({AmbiguousResources} ambiguous)" : string.Empty) : string.Empty)
        + (UnidentifiableSightings > 0 ? $", {UnidentifiableSightings} bookings with no resource id or email" : string.Empty)
        + (SkippedBookings > 0 ? $", {SkippedBookings} bookings skipped (project not returned)" : string.Empty);
}

/// <summary>
/// Orchestrates a full Hub Planner sync: fetch → Bronze capture → Silver
/// mapping. This is the only entry point Controllers should call; nothing
/// outside this file talks to IHubPlannerApiClient directly.
/// </summary>
public interface IHubPlannerSyncService
{
    Task<HubPlannerSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default);
}
