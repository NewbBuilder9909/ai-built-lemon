using ProgrammePulse.Models.ViewModels.ContractOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Admin-only, structurally — depends on IStaffRateRepository the same way
/// ReportingQueryService.BuildCostSummaryAsync does, so nothing here may be
/// called from a code path a non-Admin can reach.
/// </summary>
public interface IContractCommercialService
{
    Task<CommercialPositionViewModel> BuildCommercialPositionAsync(Guid contractKey, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One walk of the book: a row per contract, plus the deduplicated cost
    /// sitting in the contracts whose attribution is contested. The two come
    /// back together because the contested total can only be computed while
    /// the underlying time entries are in hand — see ContestedCostAggregator.
    /// </summary>
    Task<ContractSummarySetViewModel> BuildSummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
