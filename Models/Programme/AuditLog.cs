namespace ProgrammePulse.Models.Programme;

/// <summary>
/// Generic append-only audit trail for the Programme Ops domain (sync runs,
/// admin actions). Not scoped to one entity type — EntityType/EntityId say
/// what the row is about.
/// </summary>
public sealed record AuditLog
{
    public required Guid LogKey { get; init; }

    public Guid? TenantId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public int? ActorMemberId { get; init; }

    public string? DetailJson { get; init; }

    public required DateTime TimestampUtc { get; init; }
}
