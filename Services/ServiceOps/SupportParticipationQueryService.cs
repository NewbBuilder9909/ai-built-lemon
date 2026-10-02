using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Tenancy;

namespace ProgrammePulse.Services.ServiceOps;

/// <summary>
/// The support cases one person is recorded as having worked on, shown to
/// that person on their own skills page. Empty when the tenant's plan does
/// not include support evidence. A Service Ops query the web layer composes
/// with the skills page, because SkillsEvidence must not depend on Service
/// Ops: a desk-only or repository-only customer is a real configuration.
/// </summary>
public interface ISupportParticipationQueryService
{
    Task<IReadOnlyList<SupportCaseParticipant>> GetOwnParticipationAsync(Guid staffKey, Guid tenantId);
}

public sealed class SupportParticipationQueryService(
    IServiceOpsRepository serviceOpsRepository,
    IFeatureGate featureGate) : ISupportParticipationQueryService
{
    public async Task<IReadOnlyList<SupportCaseParticipant>> GetOwnParticipationAsync(Guid staffKey, Guid tenantId) =>
        await featureGate.IsEnabledAsync(ProductFeature.SupportEvidence)
            ? await serviceOpsRepository.GetParticipantsForStaffAsync(staffKey, tenantId)
            : [];
}
