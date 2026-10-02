using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.SkillsEvidence;

public interface IContributionReviewRepository
{
    // PageSize + 1 rows permits navigation without a separate count scan.
    public const int PageSize = 20;
    Task<IReadOnlyList<SkillContributionReview>> GetPageAsync(Guid staffKey, Guid tenantId, int page);
    Task<SkillContributionReview?> GetCurrentAsync(Guid linkKey, Guid tenantId);
    Task<IReadOnlyList<SkillContributionReview>> GetHistoryAsync(Guid linkKey, Guid tenantId);
    Task<IReadOnlyList<SkillContributionSource>> GetSourcesAsync(Guid staffKey, Guid tenantId, IReadOnlyCollection<Guid>? keys = null);
    Task<ResultPage<SkillContributionSource>> GetSourcePageAsync(Guid staffKey, Guid tenantId, int page);
    /// <summary>Atomic expected-revision append; validates tenant/subject references in the transaction.</summary>
    Task AppendAsync(SkillContributionReview revision, int expectedRevision);
    Task<IReadOnlyList<SkillContributionReview>> ExportAsync(Guid staffKey, Guid tenantId);
    Task EraseAsync(Guid staffKey, Guid tenantId);
}
