namespace ProgrammePulse.Models.Staff;

/// <summary>
/// A staff profile linked to an Umbraco Member. Deliberately carries no cost/rate
/// fields — those live only in <see cref="StaffRate"/> so that any code path that
/// only has a StaffProfile record in hand can never accidentally expose cost data.
/// Named StaffProfile (not Staff) to avoid colliding with the Models.Staff /
/// Services.Staff namespace segments.
/// </summary>
public sealed record StaffProfile
{
    public required Guid StaffKey { get; init; }

    public required int MemberId { get; init; }

    public required string FullName { get; init; }

    public required string Email { get; init; }

    public string? JobTitle { get; init; }

    public string? Department { get; init; }

    public string? Team { get; init; }

    public bool IsActive { get; init; } = true;

    public decimal DefaultWorkHoursPerWeek { get; init; } = 37.5m;

    public string? CalendarProvider { get; init; }

    public string? CalendarUserId { get; init; }

    /// <summary>
    /// Null until the StaffOps backfill migration runs (or for a row created
    /// before Tenancy existed and not yet re-saved) — see
    /// Services/Tenancy/TenantContextAccessor, which treats a null TenantId
    /// the same as an unresolved tenant rather than throwing.
    /// </summary>
    public Guid? TenantId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
