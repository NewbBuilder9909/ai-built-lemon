namespace ProgrammePulse.Models.Programme;

public sealed record Risk
{
    public required Guid RiskKey { get; init; }

    public Guid? TenantId { get; init; }

    public required Guid ProjectKey { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required SeverityLevel Severity { get; init; }

    public required RiskStatus Status { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
