namespace ProgrammePulse.Models.ServiceOps;

/// <summary>
/// What kind of thing a case is linked to. Provider-neutral, because the
/// case may be linked to a GitHub pull request today and a GitLab merge
/// request at the next customer.
/// </summary>
public enum LinkedArtifactType
{
    Issue = 0,
    PullRequest = 1,
    Commit = 2,
    Release = 3
}

/// <summary>
/// **How much this link is allowed to claim.** The single most important
/// enum in this feature area.
///
/// The design document is blunt about it: "An explicit Jira issue key in a
/// ticket and PR is a relationship, not proof that the PR caused the
/// ticket." The failure mode this prevents is a product that tells a
/// manager which developer caused which outage, on evidence that amounts
/// to two strings matching.
///
/// Only <see cref="ConfirmedRootCause"/> may be described as causal
/// anywhere in the UI, and it cannot be set without a named reviewer, a
/// timestamp and a rationale. Everything else is a relationship.
/// </summary>
public enum SupportLinkMethod
{
    /// <summary>
    /// The same issue key appears on the case and the artefact. Found
    /// automatically, means only "these mention each other".
    /// </summary>
    IssueKeyMatch = 0,

    /// <summary>A person said these are related. Still not a causal claim.</summary>
    ManuallyLinked = 1,

    /// <summary>
    /// A reviewer assessed the case and concluded this change caused it.
    /// The only value that licenses the word "caused", and the only one
    /// that requires a reviewer and a rationale.
    /// </summary>
    ConfirmedRootCause = 2,

    /// <summary>
    /// A reviewer looked and concluded this change did **not** cause it.
    /// Recorded rather than deleted: knowing something was investigated
    /// and ruled out is worth as much as knowing it was confirmed, and it
    /// stops the same suggestion being re-raised every sync.
    /// </summary>
    RuledOut = 3
}

/// <summary>
/// A relationship between a support case and something in source control.
///
/// Created either by an automatic issue-key match — which is a hint, and
/// says so — or by a person. Promoting one to
/// <see cref="SupportLinkMethod.ConfirmedRootCause"/> is a reviewed act
/// with a name, a time and a reason attached, and it is audited.
///
/// **There is no author field here, deliberately.** A root cause is a
/// property of a change, not of a person. The design document rules out
/// assigning blame by temporal proximity or <c>git blame</c>, and the way
/// to guarantee that is to give this record nowhere to record who to
/// blame. SupportCausalityTests fails the build if such a field appears.
/// </summary>
public sealed record SupportCodeLink
{
    public required Guid LinkKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string ExternalTicketId { get; init; }

    public required LinkedArtifactType ArtifactType { get; init; }

    /// <summary>
    /// The artefact's own identifier — an issue key, a PR id, a commit
    /// SHA. A plain string, not a typed reference, so this area does not
    /// depend on the engineering-evidence feature: a customer can connect
    /// a desk and no repository at all.
    /// </summary>
    public required string ArtifactExternalId { get; init; }

    /// <summary>Where the artefact lives — "owner/repo", or a tracker key. Free text; display only.</summary>
    public string? ArtifactSource { get; init; }

    public string? ArtifactUrl { get; init; }

    public required SupportLinkMethod Method { get; init; }

    /// <summary>Null except on a reviewed method. Enforced by SupportCodeLinkService.</summary>
    public Guid? ReviewedByStaffKey { get; init; }

    public DateTime? ReviewedAtUtc { get; init; }

    /// <summary>Why the reviewer concluded what they did. Required to confirm or rule out.</summary>
    public string? ReviewNote { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// The one place the product is permitted to say a change caused a
    /// case. Read this rather than comparing the enum by hand, so the rule
    /// lives in one expression.
    /// </summary>
    public bool IsCausalClaim => Method == SupportLinkMethod.ConfirmedRootCause;

    /// <summary>A relationship worth showing, but not evidence of cause.</summary>
    public bool IsRelationshipOnly =>
        Method is SupportLinkMethod.IssueKeyMatch or SupportLinkMethod.ManuallyLinked;

    /// <summary>Someone looked at this, either way.</summary>
    public bool IsReviewed =>
        Method is SupportLinkMethod.ConfirmedRootCause or SupportLinkMethod.RuledOut;
}

/// <summary>
/// A person's documented involvement in a case.
///
/// Note what is absent: there is no value for "caused this". The design
/// document is explicit that a person who fixed a defect can receive
/// positive resolution evidence without being labelled responsible for
/// it, and the enum enforces that by having nowhere to record blame.
/// </summary>
public enum SupportCaseRole
{
    /// <summary>Restored service. Positive evidence of incident response.</summary>
    Resolver = 0,

    /// <summary>Reviewed the fix or the root-cause assessment.</summary>
    Reviewer = 1,

    /// <summary>Took part without owning the outcome — triage, investigation, comms.</summary>
    Contributor = 2
}

/// <summary>
/// One person's documented role on one case.
///
/// <see cref="StaffKey"/> is set only through an approved
/// <see cref="DeskAgentLink"/> — the same discipline as engineering
/// evidence, for the same reason. An agent account nobody has identified
/// reaches the queue, not somebody's record.
/// </summary>
public sealed record SupportCaseParticipant
{
    public required Guid ParticipantKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string ExternalTicketId { get; init; }

    public required string ExternalAgentId { get; init; }

    public string? AgentDisplayName { get; init; }

    public required SupportCaseRole Role { get; init; }

    /// <summary>Null unless an approved <see cref="DeskAgentLink"/> exists.</summary>
    public Guid? StaffKey { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    public required DateTime IngestedAtUtc { get; init; }

    public bool IsAttributableToAPerson => StaffKey is not null;
}

/// <summary>
/// An approved mapping from a desk agent account to a staff member.
/// Scoped to the connection, like its engineering-evidence counterpart:
/// recognising an agent on one desk is not vouching for the same name on
/// another.
/// </summary>
public sealed record DeskAgentLink
{
    public required Guid LinkKey { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid ConnectionKey { get; init; }

    public required string Provider { get; init; }

    public required string ExternalAgentId { get; init; }

    public string? ExternalAgentName { get; init; }

    public required Guid StaffKey { get; init; }

    public Guid? ApprovedByStaffKey { get; init; }

    public required DateTime ApprovedAtUtc { get; init; }
}
