using ProgrammePulse.Models.SkillsEvidence;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// Key-person coverage: the manager's component map, the reviewed skills
/// underneath it, and the open actions against it.
///
/// Unlike <see cref="ISkillCoverageQueryService"/> this one names people,
/// because a continuity plan whose owner is anonymous is not a plan. It
/// therefore sits behind the narrower <c>ViewStaffSkillEvidence</c>
/// grant, not the wider <c>ViewTeamSkillCoverage</c>.
///
/// It reads the skills assertions directly rather than through the
/// aggregate view, because it needs to know *which* people hold a skill
/// in order to say whether the approved backups are among them.
/// </summary>
public interface IKeyPersonCoverageQueryService
{
    Task<KeyPersonCoverageReport> BuildAsync(Guid tenantId);
}

public sealed class KeyPersonCoverageQueryService(
    IContinuityRepository continuity,
    ISkillsEvidenceRepository skills,
    IStaffRepository staffRepository,
    TimeProvider timeProvider) : IKeyPersonCoverageQueryService
{
    public async Task<KeyPersonCoverageReport> BuildAsync(Guid tenantId)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var components = await continuity.GetComponentsAsync(tenantId);
        var backups = (await continuity.GetBackupsAsync(tenantId)).ToLookup(b => b.ComponentKey, StringComparer.OrdinalIgnoreCase);
        var actions = (await continuity.GetActionsAsync(tenantId, openOnly: false)).ToLookup(a => a.ComponentKey, StringComparer.OrdinalIgnoreCase);

        var staff = await staffRepository.GetByTenantAsync(tenantId);
        var names = staff.ToDictionary(s => s.StaffKey, s => s.FullName);
        var activeStaff = staff.Where(s => s.IsActive).Select(s => s.StaffKey).ToHashSet();

        // The soft join: where a tenant's skill taxonomy happens to use
        // the same key as their component map, the reviewed skills line
        // up. Where it does not, the column is null — which is different
        // from zero, and the report says so.
        var componentSkills = (await skills.GetSkillsAsync(tenantId, includeRetired: false))
            .Where(s => s.Kind == SkillKind.Component)
            .Select(s => s.SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assertions = (await skills.GetCurrentForTenantAsync(tenantId))
            .Where(a => activeStaff.Contains(a.StaffKey))
            .ToLookup(a => a.SkillKey, StringComparer.OrdinalIgnoreCase);

        var rows = components
            .Select(component =>
            {
                var approved = backups[component.ComponentKey]
                    .Where(b => activeStaff.Contains(b.StaffKey))
                    .Select(b => (b.StaffKey, Name: names.GetValueOrDefault(b.StaffKey, "(unknown)")))
                    .ToList();

                var open = actions[component.ComponentKey].Where(a => a.IsOpen).ToList();

                int? validatedCover = null;
                var backupsLackSkill = false;

                if (componentSkills.Contains(component.ComponentKey))
                {
                    var holders = assertions[component.ComponentKey]
                        .Where(a => a.CountsAsValidatedOn(asOf) && a.Proficiency >= ProficiencyRubric.CoverThreshold)
                        .Select(a => a.StaffKey)
                        .ToHashSet();

                    validatedCover = holders.Count;
                    // Only meaningful when there *are* approved backups:
                    // "none of zero backups holds the skill" is not a
                    // finding, it is the HasNoApprovedBackup case.
                    backupsLackSkill = approved.Count > 0 && !approved.Any(b => holders.Contains(b.StaffKey));
                }

                return new ComponentCoverageRow
                {
                    ComponentKey = component.ComponentKey,
                    DisplayName = component.DisplayName,
                    // An owner who has left is no owner. Surfacing that
                    // is the point of the view.
                    OwnerStaffKey = component.OwnerStaffKey is { } owner && activeStaff.Contains(owner) ? owner : null,
                    OwnerName = component.OwnerStaffKey is { } named ? names.GetValueOrDefault(named) : null,
                    ApprovedBackups = approved,
                    LastReviewedAtUtc = component.LastReviewedAtUtc,
                    IsReviewCurrent = component.IsReviewCurrentOn(asOf),
                    ValidatedSkillCover = validatedCover,
                    OpenActions = open.Count,
                    OverdueActions = open.Count(a => a.IsOverdueOn(asOf)),
                    BackupsLackValidatedSkill = backupsLackSkill
                };
            })
            // Most exposed first: unowned, then single-person, then the
            // rest. A risk view that sorts alphabetically buries its own
            // findings.
            .OrderByDescending(r => r.HasNoOwner)
            .ThenByDescending(r => r.IsSinglePersonExposure)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new KeyPersonCoverageReport
        {
            Components = rows,
            AsOf = asOf,
            ComponentsWithStaleReview = rows.Count(r => !r.IsReviewCurrent),
            SkillVocabularyUnaligned = componentSkills.Count == 0 && rows.Count > 0
        };
    }
}
