namespace ProgrammePulse.Models.Staff;

/// <summary>
/// One StaffOps_AuditLog row (admin actions on staff, rates, MFA, GDPR,
/// tenants). Mirrors Models/Programme/AuditLog rather than sharing it so the
/// Staff domain doesn't take a dependency on the Programme domain's model
/// just to read its own audit trail.
/// </summary>
public sealed record StaffAuditLog
{
    public required Guid LogKey { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public int? ActorMemberId { get; init; }

    public Guid? TenantId { get; init; }

    public string? DetailJson { get; init; }

    public required DateTime TimestampUtc { get; init; }
}
