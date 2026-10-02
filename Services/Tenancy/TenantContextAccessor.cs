using ProgrammePulse.Models.Tenancy;
using ProgrammePulse.Services.Staff;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// Resolves the current tenant from the signed-in member's StaffProfile,
/// read through the request-scoped ICurrentStaff (the same instance the
/// capability checks use, so the member and staff lookups happen once per
/// request), then loads the Tenant row and applies TenantAccessPolicy.
/// Scoped (one resolution per request); EnsureResolvedAsync is idempotent so
/// multiple callers in the same request only resolve once.
///
/// The decision itself is factored into the static Apply so it is unit-tested
/// directly. The lookup side now goes through ICurrentStaff, which a test can
/// fake, whereas it used to need the concrete MemberManager.
/// </summary>
public sealed class TenantContextAccessor(
    ICurrentStaff currentStaff,
    ITenantRepository tenantRepository,
    TimeProvider timeProvider) : ITenantContext
{
    private bool _resolved;

    public Guid? CurrentTenantId { get; private set; }

    public Tenant? CurrentTenant { get; private set; }

    public bool IsResolved { get; private set; }

    public bool IsBlocked { get; private set; }

    public string? BlockedReason { get; private set; }

    public async Task EnsureResolvedAsync()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;

        var staff = await currentStaff.GetProfileAsync();
        if (staff?.TenantId is null)
        {
            return;
        }

        var tenant = await tenantRepository.GetByKeyAsync(staff.TenantId.Value);
        var state = Apply(tenant, timeProvider.GetUtcNow().UtcDateTime);

        CurrentTenantId = state.CurrentTenantId;
        CurrentTenant = state.CurrentTenant;
        IsResolved = state.IsResolved;
        IsBlocked = state.IsBlocked;
        BlockedReason = state.BlockedReason;
    }

    public sealed record ResolutionState(Guid? CurrentTenantId, Tenant? CurrentTenant, bool IsResolved, bool IsBlocked, string? BlockedReason)
    {
        public static readonly ResolutionState Unresolved = new(null, null, false, false, null);
    }

    /// <summary>
    /// A StaffProfile pointing at a tenant row that no longer exists is
    /// treated as unresolved (not blocked) — same outcome as a null TenantId,
    /// and something the platform-admin console should surface, not a
    /// reason to show the member a "suspended" message that isn't true.
    /// </summary>
    public static ResolutionState Apply(Tenant? tenant, DateTime nowUtc)
    {
        if (tenant is null)
        {
            return ResolutionState.Unresolved;
        }

        var decision = TenantAccessPolicy.Evaluate(tenant, nowUtc);
        return decision.Allowed
            ? new ResolutionState(tenant.TenantKey, tenant, true, false, null)
            : new ResolutionState(null, null, false, true, decision.Reason);
    }
}
