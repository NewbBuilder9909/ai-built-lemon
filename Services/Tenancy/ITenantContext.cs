using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// The current request's tenant, resolved from the signed-in member (see
/// TenantContextAccessor). Unresolved for anonymous requests — callers on a
/// route reachable pre-login (e.g. StaffBrandingController.ThemeCss) must
/// check IsResolved and fall back to a tenant-neutral default rather than
/// dereference CurrentTenantId.
///
/// EnsureResolvedAsync must be awaited once before reading any property —
/// resolution needs the current member and repository lookups, all async,
/// so it can't happen inside a plain property getter. Same explicit
/// "await the gate, then read state" shape controllers already use for
/// IStaffAuthorizationService.HasAsync(Capability.*).
///
/// A member whose tenant exists but is Suspended/Archived/expired-trial
/// (TenantAccessPolicy) is reported as IsBlocked with IsResolved=false and
/// CurrentTenantId=null — so tenant-scoped code paths see them exactly as
/// they'd see an anonymous caller, and TenantAccessFilter turns the request
/// into a 403 before any action runs.
/// </summary>
public interface ITenantContext
{
    Task EnsureResolvedAsync();

    Guid? CurrentTenantId { get; }

    /// <summary>The full tenant record when <see cref="IsResolved"/>; drives plan entitlements.</summary>
    Tenant? CurrentTenant { get; }

    bool IsResolved { get; }

    bool IsBlocked { get; }

    /// <summary>Human-readable reason when <see cref="IsBlocked"/>, suitable for showing to the member.</summary>
    string? BlockedReason { get; }
}

public static class TenantContextExtensions
{
    /// <summary>
    /// The resolved tenant's key, or null when none is resolved (anonymous,
    /// no staff profile, or a blocked tenant). For the few paths that must
    /// decide imperatively; an action that simply needs a tenant should take
    /// a <see cref="CurrentTenantAttribute"/> parameter instead.
    /// </summary>
    public static async Task<Guid?> ResolveTenantIdAsync(this ITenantContext tenantContext)
    {
        await tenantContext.EnsureResolvedAsync();
        return tenantContext.IsResolved ? tenantContext.CurrentTenantId : null;
    }
}

public static class CurrentCallerExtensions
{
    /// <summary>
    /// The signed-in person's staff profile together with their resolved
    /// tenant, or null when either is missing. For self-service actions
    /// ("my skills", "my evidence") that act as the caller rather than on a
    /// tenant-wide record. Replaces five identical private ResolveSelfAsync
    /// helpers.
    /// </summary>
    public static async Task<(Models.Staff.StaffProfile Staff, Guid TenantId)?> ResolveSelfAsync(
        this ITenantContext tenantContext, Staff.ICurrentStaff currentStaff) =>
        await currentStaff.GetProfileAsync() is { } staff && await tenantContext.ResolveTenantIdAsync() is { } tenantId
            ? (staff, tenantId)
            : null;
}
