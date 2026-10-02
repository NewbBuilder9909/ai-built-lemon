using Umbraco.Cms.Infrastructure.Migrations;

namespace ProgrammePulse.Migrations.SkillsEvidence;

/// <summary>
/// Its own plan name, not an extension of "StaffOps" — the rule in
/// CLAUDE.md. A new feature area's tables upgrade and fail independently of
/// every other area's.
/// </summary>
public sealed class SkillsEvidenceMigrationPlan : MigrationPlan
{
    public SkillsEvidenceMigrationPlan() : base("SkillsEvidence")
    {
        From(string.Empty)
            .To<AddSkillsEvidenceTables>("2026-09-skillsevidence-01")
            .To<AddEngineeringEvidenceTables>("2026-09-skillsevidence-02")
            .To<AddContinuityTables>("2026-09-skillsevidence-03")
            .To<AddSuggestionTable>("2026-09-skillsevidence-04")
            .To<AddContributionReviews>("2026-09-skillsevidence-05")
            .To<AddEvidenceAuthorshipVerified>("2026-09-skillsevidence-06");
    }
}
