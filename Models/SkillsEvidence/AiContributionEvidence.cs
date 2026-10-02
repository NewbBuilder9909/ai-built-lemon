namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>An artefact identity, independent of the people and roles involved.</summary>
public sealed record ContributionArtifactKey(
    Guid TenantId,
    Guid ConnectionKey,
    string Provider,
    string SourceAccountId,
    string RepositoryKey,
    EvidenceSourceType SourceType,
    string ExternalId)
{
    public static ContributionArtifactKey From(EngineeringEvidence evidence) => new(
        evidence.TenantId, evidence.ConnectionKey, evidence.Provider, evidence.SourceAccountId,
        evidence.RepositoryKey, evidence.SourceType, evidence.ExternalId);
}

public enum AiAssistanceSignalKind
{
    DeveloperDeclaration,
    CommitTrailer,
    ProviderReported
}

public enum AiAssistanceWorkflow
{
    Unspecified,
    CodeGeneration,
    Review,
    Testing,
    Documentation
}

/// <summary>
/// A source's positive claim about AI assistance on one artefact. This is not
/// proof of how much code was generated, who used a tool, or time saved.
/// No signal means unknown. Repository configuration and user/day usage are
/// deliberately insufficient to create an artefact-level observation.
/// Persistence, authorization and correction history belong to a future slice.
/// </summary>
public sealed record AiAssistanceObservation
{
    public required Guid ObservationKey { get; init; }
    public required ContributionArtifactKey Artifact { get; init; }
    public required string ToolName { get; init; }
    public required AiAssistanceSignalKind SignalKind { get; init; }
    public required AiAssistanceWorkflow Workflow { get; init; }
    public required string SourceRecordId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

/// <summary>
/// One artefact in a person's portfolio, preserving their distinct roles.
/// Assistance belongs to the artefact, not automatically to this person:
/// reviewing an AI-assisted PR does not mean the reviewer generated its code.
/// </summary>
public sealed record AiContributionEvidenceRow(
    ContributionArtifactKey Artifact,
    IReadOnlyList<Guid> EvidenceKeys,
    IReadOnlyList<EvidenceRole> Roles,
    IReadOnlyList<string> SourceUrls,
    DateTime OccurredAtUtc,
    IReadOnlyList<AiAssistanceObservation> Assistance)
{
    public bool HasReportedAssistance => Assistance.Count > 0;
}

/// <summary>
/// A bounded projection only. The host must show existing connector coverage
/// alongside it; an empty result is not evidence of inactivity or non-use of AI.
/// No proficiency, productivity score or inferred hours are computed here.
/// </summary>
public sealed record AiContributionEvidencePortfolio(
    Guid TenantId,
    Guid StaffKey,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    IReadOnlyList<AiContributionEvidenceRow> Contributions);
