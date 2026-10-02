using ProgrammePulse.Models.Integrations.HubPlanner.Raw;
using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.Integrations.HubPlanner;

/// <summary>
/// The one place Hub Planner Bronze DTOs and Silver models both appear.
/// Silver/Gold code never depends on this — only HubPlannerSyncService calls
/// it. Resource-id-to-staff resolution happens in HubPlannerSyncService (via
/// IStaffIdentityResolver), not here, so this service never needs to know
/// what a Hub Planner "resource" looks like — it just takes an
/// already-resolved StaffProfile key.
/// </summary>
public interface IHubPlannerMappingService
{
    /// <summary>
    /// Upserts the single synthetic Programme every Hub Planner Project sits
    /// under — Hub Planner has no grouping above Project the way ClickUp has
    /// Spaces, so this stands in for one.
    /// </summary>
    Task<Programme> MapRootProgrammeAsync(Guid tenantId);

    Task<Project> MapProjectAsync(HubPlannerProjectDto project, Guid programmeKey, Guid tenantId);

    /// <summary>
    /// A Booking becomes a <see cref="PlannedAllocation"/> — planned time
    /// against the project, never a WorkItem. See PlannedAllocation's doc
    /// comment for why the earlier booking-as-Done-work-item mapping was
    /// removed.
    /// </summary>
    Task<PlannedAllocation> MapBookingAsync(HubPlannerBookingDto booking, Guid projectKey, Guid? resolvedStaffKey, Guid tenantId);
}
