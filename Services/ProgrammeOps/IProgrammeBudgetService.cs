using ProgrammePulse.Models.ViewModels.Reporting;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Admin-only, structurally — depends on IStaffRateRepository the same way
/// ReportingQueryService.BuildCostSummaryAsync does, so nothing here may be
/// called from a code path a non-Admin can reach.
/// </summary>
public interface IProgrammeBudgetService
{
    Task<IReadOnlyList<ProgrammeBudgetRowViewModel>> BuildBudgetSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
