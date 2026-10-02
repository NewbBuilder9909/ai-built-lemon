namespace ProgrammePulse.Models.SkillsEvidence;

public enum ContributionReviewStatus { Submitted, Accepted, Rejected, Withdrawn }

/// <summary>An immutable revision of a person's chosen example for one assertion revision.</summary>
public sealed record SkillContributionReview
{
    public required Guid LinkKey { get; init; }
    public required int Revision { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid StaffKey { get; init; }
    public required Guid AssertionKey { get; init; }
    public required Guid EvidenceKey { get; init; }
    public required ContributionReviewStatus Status { get; init; }
    public required string DemonstrationNote { get; init; }
    /// <summary>Self-declared tool involvement. Null means unknown, not unassisted.</summary>
    public string? AiTool { get; init; }
    public required AiAssistanceWorkflow AiWorkflow { get; init; }
    public string? DecisionNote { get; init; }
    public required Guid RecordedByStaffKey { get; init; }
    public required DateTime RecordedAtUtc { get; init; }
}

/// <summary>
/// Only currently mapped, non-bot evidence from a selected, active connection.
/// <paramref name="AuthorshipUnverified"/> marks a commit that does not prove
/// the person made it (see EngineeringEvidence.AuthorshipVerified).
/// </summary>
public sealed record SkillContributionSource(
    Guid EvidenceKey, string Repository, string ExternalId, string Role,
    string? Title, string? SourceUrl, DateTime OccurredAtUtc, bool AuthorshipUnverified = false);

public sealed class ContributionReviewConflictException()
    : InvalidOperationException("This example changed while you were reviewing it. Reload the page before trying again.");
