using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.ProgrammeOps;

public interface IEstimateBaselineRepository
{
    Task<IReadOnlyList<EstimateBaseline>> GetForTenantAsync(Guid tenantId);
    Task<EstimateBaseline> CaptureAsync(EstimateBaseline baseline);
    Task<EstimateBaseline?> ReviewAsync(Guid tenantId, Guid estimateBaselineKey, bool isComparable, string? note, Guid reviewedByStaffKey, DateTime reviewedAtUtc);
}
