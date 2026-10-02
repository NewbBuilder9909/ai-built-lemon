namespace ProgrammePulse.Models.Programme;

public sealed record Project
{
    public required Guid ProjectKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ProgrammeKey { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ExternalSource { get; init; }

    public string? ExternalId { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
