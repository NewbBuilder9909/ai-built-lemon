namespace ProgrammePulse.Models.Tenancy;

/// <summary>
/// A tenant/organisation. Carries the commercial lifecycle (Status, Plan,
/// trial end) needed to sell and support the product — see docs/tenancy.md.
/// No custom-domain fields exist yet; tenant resolution is member-based.
/// </summary>
public sealed record Tenant
{
    /// <summary>
    /// The one tenant every existing row is backfilled to when a TenantId
    /// column is added to a previously single-tenant table. A fixed literal
    /// rather than a database lookup, so migrations in other feature areas'
    /// plans (StaffOps, BrandingOps) never need to depend on the Tenancy
    /// plan having already run first — each plan runs independently.
    /// </summary>
    public static readonly Guid DefaultTenantKey = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public required Guid TenantKey { get; init; }

    public required string Name { get; init; }

    public required string ShortCode { get; init; }

    /// <summary>
    /// Kept in step with <see cref="Status"/> (true for Trial/Active) — a
    /// denormalised convenience for the older GetAllActiveAsync query, not
    /// an independent switch. Use <see cref="Status"/> for decisions.
    /// </summary>
    public required bool IsActive { get; init; }

    public TenantStatus Status { get; init; } = TenantStatus.Active;

    /// <summary>One of <see cref="TenantPlan"/>; decides entitlements via Services/Tenancy/PlanEntitlements.</summary>
    public string Plan { get; init; } = TenantPlan.Enterprise;

    /// <summary>Only meaningful while <see cref="Status"/> is Trial; null means an open-ended trial.</summary>
    public DateTime? TrialEndsAtUtc { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public static bool IsUsableStatus(TenantStatus status) => status is TenantStatus.Trial or TenantStatus.Active;
}
