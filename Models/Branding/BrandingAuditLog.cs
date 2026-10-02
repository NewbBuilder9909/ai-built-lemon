namespace ProgrammePulse.Models.Branding;

public sealed record BrandingAuditLog
{
    public required Guid LogKey { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public int? ActorMemberId { get; init; }

    public string? DetailJson { get; init; }

    public required DateTime TimestampUtc { get; init; }
}
