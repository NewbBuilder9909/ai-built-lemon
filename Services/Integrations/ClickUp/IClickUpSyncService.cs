namespace ProgrammePulse.Services.Integrations.ClickUp;

/// <summary>
/// UnresolvedPeople is the number of distinct ClickUp users (assignees or
/// time-entry users) this run could not match to a StaffProfile — they are
/// on the identity queue, not silently dropped.
/// </summary>
public sealed record ClickUpSyncResult(int Programmes, int Projects, int Workstreams, int WorkItems, int TimeEntries, int UnresolvedPeople = 0, int AmbiguousPeople = 0, int UnidentifiableSightings = 0)
{
    public string Describe() =>
        $"{Programmes} programmes, {Projects} projects, {Workstreams} workstreams, {WorkItems} work items, {TimeEntries} time entries"
        + (UnresolvedPeople > 0 ? $", {UnresolvedPeople} unmatched people" + (AmbiguousPeople > 0 ? $" ({AmbiguousPeople} ambiguous)" : string.Empty) : string.Empty)
        + (UnidentifiableSightings > 0 ? $", {UnidentifiableSightings} assignments with no user id or email" : string.Empty);
}

/// <summary>
/// Orchestrates a full ClickUp sync: fetch → Bronze capture → Silver
/// mapping. This is the only entry point Controllers should call; nothing
/// outside this file talks to IClickUpApiClient directly.
/// </summary>
public interface IClickUpSyncService
{
    Task<ClickUpSyncResult> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default);
}
