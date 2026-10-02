using ProgrammePulse.Models.SkillsEvidence;

namespace ProgrammePulse.Services.SkillsEvidence;

/// <summary>
/// The aggregate half of this feature: how much reviewed cover a tenant has
/// per skill, with no person in the answer.
///
/// Structurally separated from the person-level read, for the same reason
/// the Programme Overview Gold query carries no cost field (CLAUDE.md,
/// "Cost/rate data isolation is structural, not a hidden UI column"): the
/// wider <c>ViewTeamSkillCoverage</c> grant reaches this service, and this
/// service cannot return a name because <see cref="SkillCoverageRow"/> has
/// nowhere to put one. It also never touches StaffRate.
/// </summary>
public interface ISkillCoverageQueryService
{
    Task<SkillCoverageReport> BuildAsync(Guid tenantId);
}
