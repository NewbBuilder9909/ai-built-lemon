using ProgrammePulse.Services.Integrations.Abstractions;
using ProgrammePulse.Models.Integrations.HubPlanner.Raw;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// Bronze-layer HTTP boundary — the only place in the app allowed to know
/// Hub Planner's API shape or call hubplanner.com. Every method returns raw
/// JSON alongside the parsed DTO (RawEntity, reused from the ClickUp
/// integration — it's already generic, not ClickUp-specific) so the caller
/// can persist an untouched capture before any mapping happens.
/// </summary>
public interface IHubPlannerApiClient
{
    /// <summary>Returns an immutable credential-bound client without modifying shared HTTP defaults.</summary>
    IHubPlannerApiClient WithCredential(string apiToken);

    Task<IReadOnlyList<RawEntity<HubPlannerProjectDto>>> GetProjectsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<HubPlannerResourceDto>>> GetResourcesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<HubPlannerBookingDto>>> GetBookingsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawEntity<HubPlannerClientDto>>> GetClientsAsync(CancellationToken cancellationToken = default);
}
