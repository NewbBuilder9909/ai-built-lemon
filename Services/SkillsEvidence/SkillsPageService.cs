using ProgrammePulse.Services.Shared;
using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Models.ViewModels.SkillsEvidence;
using ProgrammePulse.Services.Staff;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The reads behind the skills pages: a person's own record, the review
/// queue, a person's portfolio and the taxonomy, plus the roster check a
/// reviewer's write makes. Moved out of StaffSkillsController; the commands
/// were already on ISkillAssertionService.
///
/// Nothing here raises or infers a proficiency. It shows reviewed
/// assertions and, separately, evidence, and the two never merge.
/// </summary>
public interface ISkillsPageService
{
    Task<MySkillsViewModel> BuildMySkillsAsync(Guid staffKey, Guid tenantId, PageRequest evidencePage);

    Task<SkillReviewQueueViewModel> BuildReviewQueueAsync(Guid tenantId);

    /// <summary>Null when the person is not in this tenant, so another tenant's key is a 404.</summary>
    Task<StaffSkillPortfolioViewModel?> BuildPortfolioAsync(Guid tenantId, Guid staffKey, PageRequest evidencePage);

    Task<IReadOnlyList<SkillDefinition>> GetTaxonomyAsync(Guid tenantId);

    Task<bool> IsOnRosterAsync(Guid tenantId, Guid staffKey);
}

public sealed class SkillsPageService(
    ISkillsEvidenceRepository skillsRepository,
    IStaffRepository staffRepository,
    IEvidencePortfolioQueryService evidencePortfolioQueryService,
    IFeatureGate featureGate,
    TimeProvider timeProvider) : ISkillsPageService
{
    public async Task<MySkillsViewModel> BuildMySkillsAsync(Guid staffKey, Guid tenantId, PageRequest evidencePage)
    {
        var history = await skillsRepository.GetHistoryForStaffAsync(staffKey, tenantId);
        var skills = await skillsRepository.GetSkillsAsync(tenantId, includeRetired: true);
        var rows = BuildRows(history, skills);

        var declarable = skills
            .Where(s => s.IsActive)
            .Where(s => !rows.Any(r =>
                string.Equals(r.Assertion.SkillKey, s.SkillKey, StringComparison.Ordinal)
                && r.Assertion.Status != AssertionStatus.Withdrawn))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A person's own evidence, shown to them. Reading your own record
        // needs no extra capability — ViewOwnSkills already gated this
        // page, and inspecting what a system says about you is exactly
        // the mitigation that makes collecting it defensible.
        var ownEvidence = await featureGate.IsEnabledAsync(ProductFeature.GitHubEvidence)
            ? await evidencePortfolioQueryService.BuildForStaffAsync(staffKey, tenantId, evidencePage)
            : null;

        return new MySkillsViewModel
        {
            Current = rows,
            Declarable = declarable,
            AsOf = Today(),
            OwnEvidence = ownEvidence
        };
    }

    public async Task<SkillReviewQueueViewModel> BuildReviewQueueAsync(Guid tenantId)
    {
        var assertions = await skillsRepository.GetCurrentForTenantAsync(tenantId);
        var skills = await skillsRepository.GetSkillsAsync(tenantId, includeRetired: true);
        var staff = await staffRepository.GetByTenantAsync(tenantId);

        var skillNames = skills.ToDictionary(s => s.SkillKey, s => s.Name, StringComparer.Ordinal);
        var staffNames = staff.ToDictionary(s => s.StaffKey, s => s.FullName);

        var items = assertions
            .Where(a => a.Status is AssertionStatus.Submitted or AssertionStatus.ChallengeRaised)
            // A row whose staff key is not in this tenant's roster has no
            // name to show and should not be decidable — skip rather than
            // render "(unknown)" and invite a decision on it.
            .Where(a => staffNames.ContainsKey(a.StaffKey))
            .OrderByDescending(a => a.Status == AssertionStatus.ChallengeRaised)
            .ThenBy(a => a.RecordedAtUtc)
            .Select(a => new SkillReviewItemViewModel
            {
                Assertion = a,
                StaffName = staffNames[a.StaffKey],
                SkillName = skillNames.GetValueOrDefault(a.SkillKey, a.SkillKey)
            })
            .ToList();

        return new SkillReviewQueueViewModel { Items = items, AsOf = Today() };
    }

    public async Task<StaffSkillPortfolioViewModel?> BuildPortfolioAsync(Guid tenantId, Guid staffKey, PageRequest evidencePage)
    {
        // Tenant-scoped roster lookup, so a staff key from another tenant
        // is a 404 rather than a portfolio.
        var subject = (await staffRepository.GetByTenantAsync(tenantId)).FirstOrDefault(s => s.StaffKey == staffKey);
        if (subject is null)
        {
            return null;
        }

        var history = await skillsRepository.GetHistoryForStaffAsync(staffKey, tenantId);
        var skills = await skillsRepository.GetSkillsAsync(tenantId, includeRetired: true);

        // Engineering evidence is plan-gated separately from the skills
        // matrix. A tenant without it sees the reviewed skills alone —
        // which is the whole point of keeping Slice 1 independent.
        var evidence = await featureGate.IsEnabledAsync(ProductFeature.GitHubEvidence)
            ? await evidencePortfolioQueryService.BuildForStaffAsync(staffKey, tenantId, evidencePage)
            : null;

        return new StaffSkillPortfolioViewModel
        {
            StaffKey = staffKey,
            StaffName = subject.FullName,
            JobTitle = subject.JobTitle,
            Current = BuildRows(history, skills),
            AsOf = Today(),
            Evidence = evidence
        };
    }

    public Task<IReadOnlyList<SkillDefinition>> GetTaxonomyAsync(Guid tenantId) =>
        skillsRepository.GetSkillsAsync(tenantId, includeRetired: true);

    public async Task<bool> IsOnRosterAsync(Guid tenantId, Guid staffKey) =>
        (await staffRepository.GetByTenantAsync(tenantId)).Any(s => s.StaffKey == staffKey);

    /// <summary>
    /// Pairs each current assertion with its skill's display name and the
    /// superseded rows behind it, from one history fetch — the page shows
    /// "what it says now" and "how it got there" together, and a second
    /// query per skill would be a round trip per row.
    /// </summary>
    private static List<SkillAssertionRowViewModel> BuildRows(
        IReadOnlyList<StaffSkillAssertion> history, IReadOnlyList<SkillDefinition> skills)
    {
        var names = skills.ToDictionary(s => s.SkillKey, s => s, StringComparer.Ordinal);

        return history
            .Where(a => a.IsCurrent)
            .Select(current => new SkillAssertionRowViewModel
            {
                Assertion = current,
                SkillName = names.TryGetValue(current.SkillKey, out var skill) ? skill.Name : current.SkillKey,
                Kind = names.TryGetValue(current.SkillKey, out var kind) ? kind.Kind : SkillKind.Practice,
                History = history
                    .Where(a => !a.IsCurrent && string.Equals(a.SkillKey, current.SkillKey, StringComparison.Ordinal))
                    .OrderByDescending(a => a.RecordedAtUtc)
                    .ToList()
            })
            .OrderBy(r => r.SkillName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
