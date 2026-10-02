using ProgrammePulse.Models.Staff;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// Tenant boundary: every method takes an explicit tenantId (never
/// inferred) — see docs/tenancy.md. Get*Async methods filter by tenantId;
/// CreateAsync stamps TenantId on the row it writes; UpdateAsync filters
/// its target row by tenantId too, so a requestId guessed from another
/// tenant matches nothing.
/// </summary>
public interface ILeaveRequestRepository
{
    Task<LeaveRequest?> GetByRequestKeyAsync(Guid requestKey, Guid tenantId);

    Task<IReadOnlyList<LeaveRequest>> GetForStaffAsync(Guid staffKey, Guid tenantId);

    Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid tenantId);

    Task<LeaveRequest> CreateAsync(LeaveRequest request, Guid tenantId);

    /// <exception cref="CrossTenantReferenceException">The request belongs to a different tenant or doesn't exist.</exception>
    Task UpdateAsync(LeaveRequest request, Guid tenantId);
}
