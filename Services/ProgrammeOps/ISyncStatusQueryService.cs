using ProgrammePulse.Models.ViewModels.ProgrammeOverview;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Gold-layer read of ProgrammeOps_SyncRun for the management header and
/// the platform console. Takes the list of sources from
/// ISyncSourceRegistry, so a newly registered source appears here (as
/// "never published") without this service being edited — it knows that
/// sources exist, never which vendors they are or how they sync.
/// </summary>
public interface ISyncStatusQueryService
{
    Task<IReadOnlyList<SourcePublicationStateViewModel>> GetPublicationStatesAsync(Guid tenantId);

    /// <summary>
    /// The one deliberate cross-tenant read: the Platform Admin console's
    /// support signals, showing the latest run per source across every
    /// tenant. Never call this from a tenant-facing page.
    /// </summary>
    Task<IReadOnlyList<SourcePublicationStateViewModel>> GetPublicationStatesAcrossTenantsAsync();
}
