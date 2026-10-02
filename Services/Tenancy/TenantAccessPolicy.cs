using ProgrammePulse.Models.Tenancy;

namespace ProgrammePulse.Services.Tenancy;

/// <summary>
/// The single decision "may members of this tenant use the product right
/// now?" — consumed by TenantContextAccessor (which turns a Blocked
/// decision into IsBlocked on ITenantContext) and enforced by
/// TenantAccessFilter on every /staffops request. Pure so it's exhaustively
/// unit-testable; see TenantAccessPolicyTests.
/// </summary>
public static class TenantAccessPolicy
{
    public sealed record Decision(bool Allowed, string? Reason)
    {
        public static readonly Decision Allow = new(true, null);

        public static Decision Block(string reason) => new(false, reason);
    }

    public static Decision Evaluate(Tenant tenant, DateTime nowUtc)
    {
        return tenant.Status switch
        {
            TenantStatus.Archived => Decision.Block("This organisation's account has been archived."),
            TenantStatus.Suspended => Decision.Block("This organisation's account is suspended. Contact your account manager to restore access."),
            TenantStatus.Trial when tenant.TrialEndsAtUtc is { } ends && ends <= nowUtc =>
                Decision.Block("This organisation's trial has ended. Contact your account manager to activate a plan."),
            TenantStatus.Trial or TenantStatus.Active => Decision.Allow,
            _ => Decision.Block("This organisation's account is in an unrecognised state.")
        };
    }
}
