using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The reads behind the continuity pages (the plan, the suggestion queue,
/// the processing decision), plus the roster check every command makes
/// before naming a person. Moved out of StaffContinuityController; the
/// commands themselves were already on IContinuityService and
/// ISuggestionService.
/// </summary>
public interface IContinuityPageService
{
    Task<ContinuityPlanPageViewModel> BuildPlanPageAsync(Guid tenantId, bool canManage, bool canRecordDecision);

    Task<SuggestionsPageViewModel> BuildSuggestionsPageAsync(Guid tenantId, bool canDecide);

    Task<ProcessingDecisionPageViewModel> BuildProcessingPageAsync(Guid tenantId);

    /// <summary>
    /// Whether the staff key is a person in this tenant. A key from another
    /// tenant reads as not found, never as forbidden — the same rule as
    /// everywhere else in this codebase.
    /// </summary>
    Task<bool> IsOnRosterAsync(Guid tenantId, Guid staffKey);
}

public sealed class ContinuityPageService(
    IStaffRepository staffRepository,
    IContinuityRepository continuityRepository,
    IKeyPersonCoverageQueryService coverageQueryService,
    IEnablementReadinessService readinessService,
    ISuggestionRepository suggestionRepository,
    TimeProvider timeProvider) : IContinuityPageService
{
    public async Task<ContinuityPlanPageViewModel> BuildPlanPageAsync(Guid tenantId, bool canManage, bool canRecordDecision)
    {
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        return new ContinuityPlanPageViewModel(
            await coverageQueryService.BuildAsync(tenantId),
            roster.Where(s => s.IsActive).OrderBy(s => s.FullName).ToList(),
            await continuityRepository.GetActionsAsync(tenantId, openOnly: false),
            canManage,
            canRecordDecision);
    }

    public async Task<SuggestionsPageViewModel> BuildSuggestionsPageAsync(Guid tenantId, bool canDecide)
    {
        var roster = await staffRepository.GetByTenantAsync(tenantId);
        return new SuggestionsPageViewModel(
            await suggestionRepository.GetAsync(tenantId, openOnly: false),
            roster.Where(s => s.IsActive).OrderBy(s => s.FullName).ToList(),
            roster.ToDictionary(s => s.StaffKey, s => s.FullName),
            canDecide);
    }

    public async Task<ProcessingDecisionPageViewModel> BuildProcessingPageAsync(Guid tenantId) =>
        new(
            await readinessService.BuildAsync(tenantId),
            await continuityRepository.GetLiveProcessingDecisionAsync(tenantId),
            await continuityRepository.GetProcessingDecisionHistoryAsync(tenantId),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

    public async Task<bool> IsOnRosterAsync(Guid tenantId, Guid staffKey) =>
        (await staffRepository.GetByTenantAsync(tenantId)).Any(s => s.StaffKey == staffKey);
}
