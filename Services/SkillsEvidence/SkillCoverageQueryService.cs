using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

public sealed class SkillCoverageQueryService(
    ISkillsEvidenceRepository repository,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : ISkillCoverageQueryService
{
    public async Task<SkillCoverageReport> BuildAsync(Guid tenantId)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var skills = await repository.GetSkillsAsync(tenantId, includeRetired: false);
        var assertions = await repository.GetCurrentForTenantAsync(tenantId);

        // GetByTenantAsync, not GetAllAsync: the denominator has to be this
        // tenant's people or the "how much of the team has said anything"
        // figure is meaningless.
        var staff = await staffRepository.GetByTenantAsync(tenantId);
        var activeStaff = staff.Where(s => s.IsActive).Select(s => s.StaffKey).ToHashSet();

        // An assertion from someone who has since been deactivated is not
        // cover. Counting a leaver as a maintainer is exactly the failure
        // mode this view exists to catch.
        var live = assertions.Where(a => activeStaff.Contains(a.StaffKey)).ToList();
        var bySkill = live.ToLookup(a => a.SkillKey, StringComparer.Ordinal);

        var rows = skills
            .Select(skill =>
            {
                var forSkill = bySkill[skill.SkillKey].ToList();
                var validated = forSkill.Where(a => a.CountsAsValidatedOn(asOf)).ToList();

                return new SkillCoverageRow
                {
                    SkillKey = skill.SkillKey,
                    Name = skill.Name,
                    Kind = skill.Kind,
                    ValidatedCover = validated.Count(a => a.Proficiency >= ProficiencyRubric.CoverThreshold),
                    ValidatedAtAnyLevel = validated.Count,
                    AwaitingReview = forSkill.Count(a => a.Status == AssertionStatus.Submitted),
                    // Validated but past its date — still on the record,
                    // no longer something to plan against.
                    ReviewOverdue = forSkill.Count(a =>
                        a.Status == AssertionStatus.Validated
                        && a.ReviewDueOn is not null
                        && a.ReviewDueOn.Value < asOf),
                    Challenged = forSkill.Count(a => a.Status == AssertionStatus.ChallengeRaised)
                };
            })
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staffWithSomething = live
            .Where(a => a.Status != AssertionStatus.Withdrawn)
            .Select(a => a.StaffKey)
            .ToHashSet();

        return new SkillCoverageReport
        {
            Rows = rows,
            AsOf = asOf,
            StaffInScope = activeStaff.Count,
            StaffWithNoAssertions = activeStaff.Count(key => !staffWithSomething.Contains(key))
        };
    }
}
