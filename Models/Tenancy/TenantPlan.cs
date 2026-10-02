namespace ProgrammePulse.Models.Tenancy;

/// <summary>
/// Product tiers. String constants rather than an enum, same reasoning as
/// Models/Staff/StaffRole: the name is what appears in configuration
/// (Entitlements:Plans:{name}) and in the tenant row, so it should be a
/// stable literal defined in exactly one place. Which features each plan
/// unlocks is in Services/Tenancy/PlanEntitlements.
/// </summary>
public static class TenantPlan
{
    public const string Starter = "Starter";
    public const string Professional = "Professional";
    public const string Enterprise = "Enterprise";

    public static readonly IReadOnlyList<string> All = [Starter, Professional, Enterprise];

    public static bool IsKnown(string? plan) =>
        plan is not null && All.Contains(plan, StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical casing for a plan name that passed <see cref="IsKnown"/>.</summary>
    public static string Normalize(string plan) =>
        All.First(p => string.Equals(p, plan, StringComparison.OrdinalIgnoreCase));
}
