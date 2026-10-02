using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Gold layer: reads only Silver (IProgrammeRepository) plus the existing
/// Staff domain for capacity — never IClickUpApiClient, never Bronze. This is
/// what makes Bronze/Silver replaceable by a future calendar source without
/// this class or the Programme Overview page changing at all.
/// </summary>
public interface IProgrammeOverviewQueryService
{
    Task<ProgrammeOverviewViewModel> BuildOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<ProgrammeOverviewViewModel> BuildOverviewAsync(Guid tenantId, Guid? programmeKey, Guid? customerKey, CancellationToken cancellationToken = default);
}
