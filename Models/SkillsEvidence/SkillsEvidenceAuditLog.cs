namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// One SkillsEvidence_AuditLog row. Mirrors Models/Staff/StaffAuditLog and
/// Models/ContractOps' equivalent rather than sharing one, so this domain
/// does not take a dependency on another just to read its own trail.
///
/// The action names are constants on <see cref="SkillsEvidenceAuditAction"/>
/// so the strings that a governance document promises exist are checked by
/// the compiler.
/// </summary>
public sealed record SkillsEvidenceAuditLog
{
    public required Guid LogKey { get; init; }

    public required Guid TenantId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string Action { get; init; }

    public int? ActorMemberId { get; init; }

    public string? DetailJson { get; init; }

    public required DateTime TimestampUtc { get; init; }
}

/// <summary>
/// Every audited action in this feature area, as named in
/// docs/data-governance.md's audit-coverage table.
/// </summary>
public static class SkillsEvidenceAuditAction
{
    public const string SkillCreated = "SkillCreated";
    public const string SkillRetired = "SkillRetired";
    public const string SkillReinstated = "SkillReinstated";

    public const string AssertionDeclared = "AssertionDeclared";
    public const string AssertionValidated = "AssertionValidated";
    public const string AssertionRejected = "AssertionRejected";
    public const string AssertionChallenged = "AssertionChallenged";
    public const string AssertionWithdrawn = "AssertionWithdrawn";

    /// <summary>Written by the GDPR participant with a row count, after the assertions are gone.</summary>
    public const string AssertionsErasedForSubject = "AssertionsErasedForSubject";

    // --- Engineering evidence (Slice 2). Connecting a source and mapping
    // an external account to a person are both acts that need a name
    // against them: one grants read access to a customer's repositories,
    // the other publishes someone's work to their record. ---
    public const string ConnectionCreated = "EvidenceConnectionCreated";
    public const string ConnectionUpdated = "EvidenceConnectionUpdated";
    public const string ConnectionDisconnected = "EvidenceConnectionDisconnected";
    public const string EvidenceSyncCompleted = "EvidenceSyncCompleted";
    public const string EvidenceSyncFailed = "EvidenceSyncFailed";
    public const string ActorLinkApproved = "EvidenceActorLinkApproved";
    public const string ActorLinkRevoked = "EvidenceActorLinkRevoked";
    public const string EvidenceErasedForSubject = "EvidenceErasedForSubject";

    // --- Continuity (Slice 4). Ownership and cover are management
    // decisions with names attached, and the processing decision is the
    // customer's own legal attestation — all four need an audit trail an
    // auditor can read back. ---
    public const string ComponentDeclared = "ComponentDeclared";
    public const string ComponentUpdated = "ComponentUpdated";
    public const string ComponentReviewed = "ComponentReviewed";
    public const string ComponentRetired = "ComponentRetired";
    public const string BackupApproved = "ComponentBackupApproved";
    public const string BackupRemoved = "ComponentBackupRemoved";
    public const string CoverageActionRaised = "CoverageActionRaised";
    public const string CoverageActionClosed = "CoverageActionClosed";
    public const string ProcessingDecisionRecorded = "EvidenceProcessingDecisionRecorded";
    public const string ProcessingDecisionWithdrawn = "EvidenceProcessingDecisionWithdrawn";
    public const string ContinuityDetachedForSubject = "ContinuityDetachedForSubject";

    // --- Suggestions (Slice 5). A suggestion is only ever a proposal,
    // so what is audited is the human decision about it — who accepted
    // or dismissed what, which is the accountability the design document
    // asks for when a suggestion becomes an assertion. ---
    public const string SuggestionsGenerated = "SuggestionsGenerated";
    public const string SuggestionAccepted = "SuggestionAccepted";
    public const string SuggestionDismissed = "SuggestionDismissed";
    public const string SuggestionsErasedForSubject = "SuggestionsErasedForSubject";

    public const string EntityTypeSkill = "Skill";
    public const string EntityTypeAssertion = "SkillAssertion";
    public const string EntityTypeStaff = "Staff";
    public const string EntityTypeConnection = "EvidenceConnection";
    public const string EntityTypeActorLink = "EvidenceActorLink";
    public const string EntityTypeComponent = "Component";
    public const string EntityTypeCoverageAction = "CoverageAction";
    public const string EntityTypeProcessingDecision = "EvidenceProcessingDecision";
    public const string EntityTypeSuggestion = "Suggestion";
}
