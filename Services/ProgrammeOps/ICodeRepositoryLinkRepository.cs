using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.ProgrammeOps;

/// <summary>
/// Declared repository → project links (ProgrammeOps_CodeRepositoryLink).
/// Its own repository rather than more methods on IProgrammeRepository: a
/// separate aggregate, like identity links. Every method takes the tenant
/// and applies it in the SQL.
/// </summary>
public interface ICodeRepositoryLinkRepository
{
    /// <summary>One page of live links, ordered by provider, account and repository.</summary>
    Task<ResultPage<CodeRepositoryLink>> GetLivePageAsync(Guid tenantId, PageRequest page);

    /// <summary>
    /// Every live link. Bounded by the number of repositories a tenant has,
    /// not by activity; used for the gap report and, later, for resolving
    /// which customers a repository serves.
    /// </summary>
    Task<IReadOnlyList<CodeRepositoryLink>> GetLiveAsync(Guid tenantId);

    Task<CodeRepositoryLink?> GetLiveByKeyAsync(Guid linkKey, Guid tenantId);

    /// <summary>
    /// Creates the link unless a live one already joins this repository and
    /// project, in which case it returns false. Throws
    /// CrossTenantReferenceException if the project is not the tenant's.
    /// </summary>
    Task<bool> TryCreateAsync(CodeRepositoryLink link);

    /// <summary>Ends a live link (never deletes it); false if there was none.</summary>
    Task<bool> EndAsync(Guid linkKey, Guid tenantId, Guid? removedByStaffKey, DateTime nowUtc);
}
