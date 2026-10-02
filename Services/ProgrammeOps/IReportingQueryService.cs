using ProgrammePulse.Models.ViewModels.Reporting;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Gold-layer PMO reporting: effort variance, contributor capacity/
/// utilisation, and customer rollup. BuildHubAsync never touches
/// IStaffRateRepository — BuildCostSummaryAsync is the one method that does,
/// and it must only ever be called after a ViewCommercials capability check, same
/// principle as ProgrammeOverviewQueryService keeping cost out of its view
/// model entirely.
/// </summary>
public interface IReportingQueryService
{
    Task<ReportingHubViewModel> BuildHubAsync(
        DateOnly periodStart, DateOnly periodEnd, string? teamFilter, Guid tenantId,
        Guid? programmeFilter = null, Guid? customerFilter = null, CancellationToken cancellationToken = default);

    Task<CostSummaryViewModel> BuildCostSummaryAsync(DateOnly periodStart, DateOnly periodEnd, Guid tenantId, CancellationToken cancellationToken = default);
}
