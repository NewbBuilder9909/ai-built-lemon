namespace ProgrammePulse.Services.Tenancy;

public enum FeatureGateOutcome
{
    Enabled,

    /// <summary>The tenant resolved, but its plan does not include the feature — a commercial "upgrade" message.</summary>
    NotInPlan,

    /// <summary>
    /// No tenant could be resolved for the caller (anonymous, or a
    /// StaffProfile with no/dangling tenant). Fails closed: nothing
    /// plan-gated is reachable until the member is attached to a tenant —
    /// an operator fix, not a plan upgrade, so the message is different.
    /// </summary>
    TenantUnresolved
}

public sealed record FeatureGateDecision(bool Enabled, FeatureGateOutcome Outcome, string? Plan)
{
    public static readonly FeatureGateDecision Unresolved = new(false, FeatureGateOutcome.TenantUnresolved, null);
}

/// <summary>
/// "Does the current tenant's plan include this feature?" — the hook
/// controllers ([RequireFeature]) and views (nav links) ask. A commercial
/// control layered on top of authorization, never instead of it: every
/// role check (IStaffAuthorizationService) still applies independently.
/// An unresolved tenant is answered <b>closed</b> (see
/// <see cref="FeatureGateOutcome.TenantUnresolved"/>) — since every staff
/// row is stamped with a tenant at onboarding and backfilled by migration,
/// an unresolved tenant is an anomaly to surface, not a reason to grant
/// every feature.
/// </summary>
public interface IFeatureGate
{
    Task<FeatureGateDecision> EvaluateAsync(string feature);

    Task<bool> IsEnabledAsync(string feature);
}
