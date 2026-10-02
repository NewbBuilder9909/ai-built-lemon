using ProgrammePulse.Models.ViewModels.ContractOps;

namespace ProgrammePulse.Services.ContractOps;

/// <summary>
/// Admin-only, same as IContractCommercialService — this is a rollup of the
/// same commercially sensitive figures, not a separate data source.
/// </summary>
public interface IPortfolioMarginService
{
    Task<PortfolioMarginViewModel> BuildAsync(Guid tenantId);
}
