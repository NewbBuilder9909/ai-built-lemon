using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Team Lead+ (same gate as the Reporting Hub) — carries no cost/rate
/// field, built from a team-unfiltered BuildHubAsync call.
/// </summary>
public interface IReportingSnapshotService
{
    /// <summary>
    /// Records the portfolio as it stands now against the period given. Refused
    /// for a period that ends in the future or more than
    /// <see cref="ReportingSnapshotService.CaptureWindowDays"/> days ago, because
    /// the figures are always today's and would be filed under the wrong period.
    /// </summary>
    Task<CommandResult> CaptureAsync(DateOnly periodStart, DateOnly periodEnd, Guid? capturedByStaffKey, Guid tenantId);

    Task<IReadOnlyList<ReportingSnapshot>> GetHistoryAsync(Guid tenantId);
}
