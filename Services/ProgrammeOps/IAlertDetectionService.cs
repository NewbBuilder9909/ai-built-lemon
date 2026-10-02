using ProgrammePulse.Models.Programme;
using ProgrammePulse.Models.ViewModels.Reporting;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Team Lead+ (same gate as the Reporting Hub) — carries no cost/rate
/// field.
/// </summary>
public interface IAlertDetectionService
{
    /// <summary>
    /// Detects across the whole tenant (no team filter) over the last
    /// <see cref="AlertDetectionService.DetectionWindowDays"/> days. This is
    /// what the sync and the Alerts page call.
    /// </summary>
    Task DetectForTenantAsync(Guid tenantId, DateTime nowUtc);

    /// <summary>
    /// Detects current threshold breaches (a workstream with a blocked work
    /// item, a contributor with negative residual capacity) and raises a
    /// new Alert for any that don't already have an open one. Repeat calls
    /// are deduplicated against the open alerts, and UX_ProgrammeOps_Alert_open
    /// makes a duplicate from two concurrent calls impossible (finding B3).
    /// </summary>
    Task DetectAndRaiseAsync(IReadOnlyList<ContributorCapacityRowViewModel> contributorCapacity, DateTime nowUtc, Guid tenantId);

    /// <summary>One page of open alerts, newest first (the Alerts page).</summary>
    Task<ResultPage<Alert>> GetOpenAlertsPageAsync(Guid tenantId, PageRequest page, string? teamFilter = null, CancellationToken cancellationToken = default);

    /// <summary>How many alerts are open (the Reporting Hub badge).</summary>
    Task<int> CountOpenAlertsAsync(Guid tenantId, string? teamFilter = null, CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(Guid alertKey, Guid? acknowledgedByStaffKey, DateTime nowUtc, Guid tenantId);
}
